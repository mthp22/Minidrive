using Minidrive.Application.Abstractions;
using Minidrive.Application.Abstractions.Persistence;
using Minidrive.Application.Abstractions.Storage;
using Minidrive.Application.Common;
using Minidrive.Application.Exceptions;
using Minidrive.Application.Models;
using Minidrive.Application.Options;
using Minidrive.Domain.Entities;
using Minidrive.Domain.Enums;
using Minidrive.Domain.Exceptions;
using Minidrive.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Minidrive.Application.Services;

/// <summary>
/// Orchestrates the three-phase multipart upload pipeline: initiate, pre-sign parts and complete
/// (plus abort). MongoDB and object storage do not share a transaction, so every step is designed
/// to be idempotent or explicitly recoverable rather than atomically consistent.
/// </summary>
public sealed class UploadService
{
    private const string DefaultContentType = "application/octet-stream";

    private readonly INodeRepository _nodes;
    private readonly IUploadSessionRepository _sessions;
    private readonly IObjectStorageService _storage;
    private readonly IStorageCleanupQueue _cleanupQueue;
    private readonly IIdGenerator _ids;
    private readonly TimeProvider _clock;
    private readonly UploadOptions _options;
    private readonly ILogger<UploadService> _logger;

    public UploadService(
        INodeRepository nodes,
        IUploadSessionRepository sessions,
        IObjectStorageService storage,
        IStorageCleanupQueue cleanupQueue,
        IIdGenerator ids,
        TimeProvider clock,
        IOptions<UploadOptions> options,
        ILogger<UploadService> logger)
    {
        _nodes = nodes;
        _sessions = sessions;
        _storage = storage;
        _cleanupQueue = cleanupQueue;
        _ids = ids;
        _clock = clock;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Phase A: reserves a pending file, creates the session and starts the provider upload.</summary>
    public async Task<InitiateUploadResponse> InitiateAsync(
        string userId,
        InitiateUploadRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _clock.GetUtcNow();
        var fileName = NodeName.Validate(request.FileName);

        if (request.SizeBytes < 0)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["sizeBytes"] = ["The size must not be negative."],
            });
        }

        if (request.SizeBytes > _options.MaxFileSizeBytes)
        {
            throw new PayloadTooLargeException(
                $"The file exceeds the maximum permitted size of {_options.MaxFileSizeBytes} bytes.");
        }

        var partCount = UploadSession.CalculatePartCount(request.SizeBytes, _options.ChunkSizeBytes);
        if (partCount > UploadOptions.ProviderMaxPartCount)
        {
            throw new PayloadTooLargeException(
                "The file requires more parts than the storage provider supports for a single upload.");
        }

        var contentType = string.IsNullOrWhiteSpace(request.ContentType) ? DefaultContentType : request.ContentType.Trim();
        var parentPath = await ResolveParentPathAsync(userId, request.ParentId, cancellationToken);

        if (await _nodes.SiblingNameExistsAsync(userId, request.ParentId, fileName, excludeNodeId: null, cancellationToken))
        {
            throw new NameConflictException(fileName);
        }

        await EnsureQuotaAsync(userId, request.SizeBytes, cancellationToken);

        var fileId = _ids.NewId();
        var sessionId = _ids.NewId();
        var storageKey = ObjectKeys.ForUserFile(userId, fileId);

        var pendingNode = Node.CreatePendingFile(
            fileId,
            userId,
            request.ParentId,
            parentPath,
            fileName,
            contentType,
            storageKey,
            now);

        // Reserve the name first: the unique sibling-name index guarantees only one initiation
        // can hold a given name in a folder at a time.
        await _nodes.InsertAsync(pendingNode, cancellationToken);

        var session = UploadSession.Create(
            sessionId,
            userId,
            fileId,
            request.ParentId,
            storageKey,
            contentType,
            request.SizeBytes,
            _options.ChunkSizeBytes,
            now,
            _options.UploadSessionLifetime);

        try
        {
            await _sessions.InsertAsync(session, cancellationToken);
        }
        catch
        {
            await SafeDeletePendingNodeAsync(userId, fileId, cancellationToken);
            throw;
        }

        string providerUploadId;
        try
        {
            providerUploadId = await _storage.InitiateMultipartUploadAsync(storageKey, contentType, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to initiate the multipart upload for session {UploadSessionId}.",
                sessionId);
            await RollbackInitiationAsync(userId, sessionId, fileId, storageKey, providerUploadId: null, cancellationToken);
            throw;
        }

        var activated = await _sessions.TransitionAsync(
            new UploadSessionTransition(sessionId, userId, UploadStatus.Initiating, UploadStatus.Active, ProviderUploadId: providerUploadId),
            now,
            cancellationToken);

        if (activated is null)
        {
            _logger.LogWarning(
                "Upload session {UploadSessionId} could not be activated; rolling back.",
                sessionId);
            await RollbackInitiationAsync(userId, sessionId, fileId, storageKey, providerUploadId, cancellationToken);
            throw new ConflictException("upload.state", "The upload session could not be activated.");
        }

        _logger.LogInformation(
            "Initiated upload session {UploadSessionId} for file {FileId} ({SizeBytes} bytes, {PartCount} parts).",
            sessionId,
            fileId,
            request.SizeBytes,
            partCount);

        return new InitiateUploadResponse(
            UploadId: sessionId,
            FileId: fileId,
            FileName: fileName,
            ChunkSizeBytes: _options.ChunkSizeBytes,
            PartNumberMinimum: 1,
            PartNumberMaximum: partCount,
            ExpectedPartCount: partCount,
            ExpiresAtUtc: activated.ExpiresAtUtc,
            Status: activated.Status.ToString());
    }

    /// <summary>Phase B: pre-signs the upload of a single part, using trusted server-side state.</summary>
    public async Task<PartUploadUrlResponse> CreatePartUrlAsync(
        string userId,
        string uploadId,
        int partNumber,
        CancellationToken cancellationToken)
    {
        var session = await GetOwnedSessionAsync(userId, uploadId, cancellationToken);
        var now = _clock.GetUtcNow();

        if (session.HasExpired(now))
        {
            throw new ConflictException("upload.expired", "The upload session has expired.");
        }

        if (session.Status != UploadStatus.Active)
        {
            throw new ConflictException(
                "upload.notactive",
                $"The upload session is '{session.Status}' and cannot accept new parts.");
        }

        if (partNumber < 1 || partNumber > session.ExpectedPartCount)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["partNumber"] = [$"The part number must be between 1 and {session.ExpectedPartCount}."],
            });
        }

        var presigned = await _storage.CreatePresignedPartUrlAsync(
            session.StorageKey,
            session.ProviderUploadId,
            partNumber,
            _options.PresignedUrlExpiration,
            cancellationToken);

        return new PartUploadUrlResponse(partNumber, presigned.Url, presigned.ExpiresAtUtc, presigned.RequiredHeaders);
    }

    /// <summary>Phase C: completes the provider upload and finalises the file node.</summary>
    public async Task<NodeDetails> CompleteAsync(
        string userId,
        string uploadId,
        CompleteUploadRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var session = await GetOwnedSessionAsync(userId, uploadId, cancellationToken);
        var now = _clock.GetUtcNow();

        // Idempotency: a repeated completion of a finished upload returns the existing file.
        if (session.Status == UploadStatus.Completed)
        {
            var existing = await _nodes.FindAsync(userId, session.FileNodeId, includePending: false, includeDeleted: false, cancellationToken);
            if (existing is not null)
            {
                return NodeModelMapper.ToDetails(existing);
            }

            throw new ConflictException("upload.completed", "The upload session is already completed.");
        }

        if (session.Status != UploadStatus.Active)
        {
            throw new ConflictException(
                "upload.notactive",
                $"The upload session is '{session.Status}' and cannot be completed.");
        }

        var parts = MultipartPartRules.ValidateAndOrder(
            request.Parts?.Select(part => new UploadedPart(part.PartNumber, part.ETag)).ToArray(),
            session.ExpectedPartCount);

        // Claim the session. Only one concurrent request can move Active -> Completing.
        var claimed = await _sessions.TransitionAsync(
            new UploadSessionTransition(session.Id, userId, UploadStatus.Active, UploadStatus.Completing),
            now,
            cancellationToken);

        if (claimed is null)
        {
            var node = await _nodes.FindAsync(userId, session.FileNodeId, includePending: true, includeDeleted: false, cancellationToken);
            if (node is not null && node.IsReady)
            {
                return NodeModelMapper.ToDetails(node);
            }

            throw new ConflictException("upload.inprogress", "The upload session is already being completed.");
        }

        CompletedUpload completed;
        try
        {
            completed = await _storage.CompleteMultipartUploadAsync(
                session.StorageKey,
                session.ProviderUploadId,
                parts,
                cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Completing the multipart upload failed for session {UploadSessionId}.",
                session.Id);
            await MarkFailedAsync(userId, session.Id, "multipart completion failed", cancellationToken);
            throw;
        }

        // Trust the provider's metadata over any client-reported byte count.
        var metadata = await _storage.GetObjectMetadataAsync(session.StorageKey, cancellationToken);
        var finalSize = metadata?.SizeBytes ?? session.ExpectedSizeBytes;

        try
        {
            MultipartPartRules.EnsureMinimumNonFinalPartSizes(
                finalSize,
                session.ExpectedPartCount,
                UploadOptions.MinimumPartSizeBytes);

            if (metadata is not null && metadata.SizeBytes != session.ExpectedSizeBytes)
            {
                throw new ValidationFailedException(new Dictionary<string, string[]>
                {
                    ["sizeBytes"] =
                    [
                        $"The uploaded size ({metadata.SizeBytes} bytes) does not match the expected size " +
                        $"({session.ExpectedSizeBytes} bytes).",
                    ],
                });
            }
        }
        catch (ValidationFailedException)
        {
            // The object exists (the multipart upload completed) but is not usable. Delete it via the
            // cleanup queue, fail the session and let the client start a new upload.
            await _cleanupQueue.EnqueueAsync(userId, session.StorageKey, cancellationToken);
            await MarkFailedAsync(userId, session.Id, "uploaded size did not match the expected size", cancellationToken);
            throw;
        }

        var eTag = metadata?.ETag ?? completed.ETag;

        if (!await _nodes.TryMarkReadyAsync(userId, session.FileNodeId, finalSize, eTag, now, cancellationToken))
        {
            // The node was already finalised (retry) or is missing; the session transition still
            // records the authoritative outcome.
            _logger.LogWarning(
                "Pending file node {FileId} was not in a pending state when completing session {UploadSessionId}.",
                session.FileNodeId,
                session.Id);
        }

        await _sessions.TransitionAsync(
            new UploadSessionTransition(
                session.Id,
                userId,
                UploadStatus.Completing,
                UploadStatus.Completed,
                FinalETag: eTag,
                CompletedAtUtc: now),
            now,
            cancellationToken);

        var file = await _nodes.FindAsync(userId, session.FileNodeId, includePending: true, includeDeleted: false, cancellationToken);
        if (file is null)
        {
            throw new NotFoundException("The uploaded file could not be found after completion.");
        }

        _logger.LogInformation(
            "Completed upload session {UploadSessionId} as file {FileId} ({SizeBytes} bytes).",
            session.Id,
            file.Id,
            file.SizeBytes);

        return NodeModelMapper.ToDetails(file);
    }

    /// <summary>Aborts an upload session, discarding uploaded parts and the pending node.</summary>
    public async Task<AbortUploadResponse> AbortAsync(
        string userId,
        string uploadId,
        string? reason,
        CancellationToken cancellationToken)
    {
        var session = await GetOwnedSessionAsync(userId, uploadId, cancellationToken);
        var now = _clock.GetUtcNow();

        // Terminal sessions are reported as-is so a retried abort is harmless.
        if (session.IsTerminal)
        {
            return new AbortUploadResponse(session.Id, session.Status.ToString());
        }

        var claimed = await _sessions.TransitionAsync(
            new UploadSessionTransition(session.Id, userId, session.Status, UploadStatus.Aborting),
            now,
            cancellationToken);

        if (claimed is null)
        {
            var latest = await _sessions.FindAsync(userId, uploadId, cancellationToken);
            throw new ConflictException(
                "upload.abort.conflict",
                $"The upload session is '{latest?.Status.ToString() ?? "unknown"}' and cannot be aborted.");
        }

        if (!string.IsNullOrEmpty(session.ProviderUploadId))
        {
            try
            {
                await _storage.AbortMultipartUploadAsync(session.StorageKey, session.ProviderUploadId, cancellationToken);
            }
            catch (Exception exception)
            {
                // The pending node is still removed so the name is released; any orphaned parts will be
                // reclaimed by the provider's own abort-on-expiry policy.
                _logger.LogWarning(
                    exception,
                    "Aborting the provider multipart upload failed for session {UploadSessionId}.",
                    session.Id);
            }
        }

        await SafeDeletePendingNodeAsync(userId, session.FileNodeId, cancellationToken);

        await _sessions.TransitionAsync(
            new UploadSessionTransition(session.Id, userId, UploadStatus.Aborting, UploadStatus.Aborted, CompletedAtUtc: now),
            now,
            cancellationToken);

        _logger.LogInformation(
            "Aborted upload session {UploadSessionId} (reason provided: {ReasonProvided}).",
            session.Id,
            !string.IsNullOrWhiteSpace(reason));

        return new AbortUploadResponse(session.Id, UploadStatus.Aborted.ToString());
    }

    private async Task EnsureQuotaAsync(string userId, long requestedBytes, CancellationToken cancellationToken)
    {
        if (_options.QuotaBytesPerUser <= 0)
        {
            return;
        }

        var used = await _nodes.SumUserFileSizeAsync(userId, cancellationToken);
        if (used + requestedBytes > _options.QuotaBytesPerUser)
        {
            throw new QuotaExceededException(_options.QuotaBytesPerUser, used, requestedBytes);
        }
    }

    private async Task<string?> ResolveParentPathAsync(string userId, string? parentId, CancellationToken cancellationToken)
    {
        if (parentId is null)
        {
            return null;
        }

        var parent = await _nodes.FindAsync(userId, parentId, includePending: false, includeDeleted: false, cancellationToken);
        if (parent is null || !parent.IsFolder)
        {
            throw new NotFoundException("The destination folder was not found.");
        }

        return parent.MaterializedPath;
    }

    private async Task<UploadSession> GetOwnedSessionAsync(string userId, string uploadId, CancellationToken cancellationToken)
    {
        var session = await _sessions.FindAsync(userId, uploadId, cancellationToken);
        if (session is null)
        {
            throw new NotFoundException("The upload session was not found.");
        }

        return session;
    }

    private async Task MarkFailedAsync(string userId, string sessionId, string reason, CancellationToken cancellationToken)
    {
        await SafeTransitionAsync(
            new UploadSessionTransition(sessionId, userId, UploadStatus.Completing, UploadStatus.Failed, FailureReason: reason),
            cancellationToken);
        _logger.LogWarning("Upload session {UploadSessionId} marked as failed: {Reason}.", sessionId, reason);
    }

    private async Task RollbackInitiationAsync(
        string userId,
        string sessionId,
        string fileId,
        string storageKey,
        string? providerUploadId,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(providerUploadId))
        {
            try
            {
                await _storage.AbortMultipartUploadAsync(storageKey, providerUploadId, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Cleanup abort failed for provider upload belonging to session {UploadSessionId}.",
                    sessionId);
            }
        }

        await SafeTransitionAsync(
            new UploadSessionTransition(sessionId, userId, UploadStatus.Initiating, UploadStatus.Failed, FailureReason: "initiation failed"),
            cancellationToken);

        await SafeDeletePendingNodeAsync(userId, fileId, cancellationToken);
    }

    private async Task SafeDeletePendingNodeAsync(string userId, string fileId, CancellationToken cancellationToken)
    {
        try
        {
            await _nodes.DeleteAsync(userId, fileId, cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to remove pending file node {FileId}.", fileId);
        }
    }

    private async Task SafeTransitionAsync(UploadSessionTransition transition, CancellationToken cancellationToken)
    {
        try
        {
            await _sessions.TransitionAsync(transition, _clock.GetUtcNow(), cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "Failed to apply upload session transition {From} -> {To}.",
                transition.ExpectedStatus,
                transition.NewStatus);
        }
    }
}
