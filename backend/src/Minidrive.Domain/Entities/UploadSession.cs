using Minidrive.Domain.Enums;
using Minidrive.Domain.Exceptions;

namespace Minidrive.Domain.Entities;

/// <summary>
/// Tracks a multipart upload from initiation to completion (or abort/expiry).
/// </summary>
/// <remarks>
/// The session is the trusted, server-side record of an upload. Clients only ever see the
/// session identifier; the provider upload id and the storage key never leave the server,
/// which is what prevents a client from uploading to an arbitrary object key.
/// </remarks>
public sealed class UploadSession
{
    private static readonly Dictionary<UploadStatus, UploadStatus[]> AllowedTransitions = new()
    {
        [UploadStatus.Initiating] = [UploadStatus.Active, UploadStatus.Aborting, UploadStatus.Failed, UploadStatus.Expired],
        [UploadStatus.Active] = [UploadStatus.Completing, UploadStatus.Aborting, UploadStatus.Failed, UploadStatus.Expired],
        [UploadStatus.Completing] = [UploadStatus.Completed, UploadStatus.Failed],
        [UploadStatus.Aborting] = [UploadStatus.Aborted, UploadStatus.Expired, UploadStatus.Failed],
        [UploadStatus.Completed] = [],
        [UploadStatus.Aborted] = [],
        [UploadStatus.Failed] = [],
        [UploadStatus.Expired] = [],
    };

    internal UploadSession(
        string id,
        string userId,
        string fileNodeId,
        string? parentId,
        string storageKey,
        string providerUploadId,
        string? contentType,
        long expectedSizeBytes,
        long chunkSizeBytes,
        int expectedPartCount,
        UploadStatus status,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset expiresAtUtc,
        DateTimeOffset? completedAtUtc,
        string? finalETag,
        string? failureReason)
    {
        Id = id;
        UserId = userId;
        FileNodeId = fileNodeId;
        ParentId = parentId;
        StorageKey = storageKey;
        ProviderUploadId = providerUploadId;
        ContentType = contentType;
        ExpectedSizeBytes = expectedSizeBytes;
        ChunkSizeBytes = chunkSizeBytes;
        ExpectedPartCount = expectedPartCount;
        Status = status;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        ExpiresAtUtc = expiresAtUtc;
        CompletedAtUtc = completedAtUtc;
        FinalETag = finalETag;
        FailureReason = failureReason;
    }

    public string Id { get; }

    public string UserId { get; }

    /// <summary>Identifier of the pending file node reserved by this upload.</summary>
    public string FileNodeId { get; }

    /// <summary>Destination folder identifier, or <see langword="null"/> for the drive root.</summary>
    public string? ParentId { get; }

    /// <summary>Opaque, server-generated object key.</summary>
    public string StorageKey { get; }

    /// <summary>Provider multipart upload id. Empty until the provider upload is created.</summary>
    public string ProviderUploadId { get; private set; }

    public string? ContentType { get; }

    public long ExpectedSizeBytes { get; }

    public long ChunkSizeBytes { get; }

    public int ExpectedPartCount { get; }

    public UploadStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset ExpiresAtUtc { get; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public string? FinalETag { get; private set; }

    public string? FailureReason { get; private set; }

    public bool IsTerminal =>
        Status is UploadStatus.Completed or UploadStatus.Aborted or UploadStatus.Failed or UploadStatus.Expired;

    /// <summary>True when the session is still open and has passed its expiry timestamp.</summary>
    public bool HasExpired(DateTimeOffset now) => !IsTerminal && now >= ExpiresAtUtc;

    /// <summary>Creates a session in the <see cref="UploadStatus.Initiating"/> state.</summary>
    public static UploadSession Create(
        string id,
        string userId,
        string fileNodeId,
        string? parentId,
        string storageKey,
        string? contentType,
        long expectedSizeBytes,
        long chunkSizeBytes,
        DateTimeOffset now,
        TimeSpan lifetime)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        if (expectedSizeBytes < 0)
        {
            throw new DomainException("upload.size.invalid", "An upload size cannot be negative.");
        }

        if (chunkSizeBytes <= 0)
        {
            throw new DomainException("upload.chunk.invalid", "An upload chunk size must be positive.");
        }

        return new UploadSession(
            id: id,
            userId: userId,
            fileNodeId: fileNodeId,
            parentId: parentId,
            storageKey: storageKey,
            providerUploadId: string.Empty,
            contentType: string.IsNullOrWhiteSpace(contentType) ? null : contentType.Trim(),
            expectedSizeBytes: expectedSizeBytes,
            chunkSizeBytes: chunkSizeBytes,
            expectedPartCount: CalculatePartCount(expectedSizeBytes, chunkSizeBytes),
            status: UploadStatus.Initiating,
            createdAtUtc: now,
            updatedAtUtc: now,
            expiresAtUtc: now.Add(lifetime),
            completedAtUtc: null,
            finalETag: null,
            failureReason: null);
    }

    /// <summary>Number of parts the upload is expected to produce (at least one).</summary>
    public static int CalculatePartCount(long expectedSizeBytes, long chunkSizeBytes)
    {
        if (chunkSizeBytes <= 0)
        {
            throw new DomainException("upload.chunk.invalid", "An upload chunk size must be positive.");
        }

        var parts = (expectedSizeBytes + chunkSizeBytes - 1) / chunkSizeBytes;
        return (int)Math.Max(1, parts);
    }

    /// <summary>Records the provider multipart upload id and moves to <see cref="UploadStatus.Active"/>.</summary>
    public void Activate(string providerUploadId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerUploadId);
        EnsureTransition(UploadStatus.Active);

        ProviderUploadId = providerUploadId;
        Status = UploadStatus.Active;
        UpdatedAtUtc = now;
    }

    /// <summary>Claims the session for completion; prevents concurrent completion attempts.</summary>
    public void BeginCompleting(DateTimeOffset now)
    {
        EnsureTransition(UploadStatus.Completing);
        Status = UploadStatus.Completing;
        UpdatedAtUtc = now;
    }

    /// <summary>Marks the upload as successfully completed.</summary>
    public void MarkCompleted(string? finalETag, DateTimeOffset now)
    {
        EnsureTransition(UploadStatus.Completed);

        Status = UploadStatus.Completed;
        FinalETag = string.IsNullOrWhiteSpace(finalETag) ? null : finalETag;
        CompletedAtUtc = now;
        UpdatedAtUtc = now;
    }

    /// <summary>Marks the session as being aborted.</summary>
    public void BeginAbort(DateTimeOffset now)
    {
        EnsureTransition(UploadStatus.Aborting);
        Status = UploadStatus.Aborting;
        UpdatedAtUtc = now;
    }

    /// <summary>Marks the abort as finished.</summary>
    public void MarkAborted(DateTimeOffset now)
    {
        EnsureTransition(UploadStatus.Aborted);
        Status = UploadStatus.Aborted;
        CompletedAtUtc = now;
        UpdatedAtUtc = now;
    }

    /// <summary>Marks the session as failed, recording a non-sensitive reason.</summary>
    public void MarkFailed(string reason, DateTimeOffset now)
    {
        EnsureTransition(UploadStatus.Failed);

        Status = UploadStatus.Failed;
        FailureReason = reason;
        CompletedAtUtc = now;
        UpdatedAtUtc = now;
    }

    /// <summary>Marks an open session as expired after cleanup.</summary>
    public void MarkExpired(DateTimeOffset now)
    {
        EnsureTransition(UploadStatus.Expired);

        Status = UploadStatus.Expired;
        CompletedAtUtc = now;
        UpdatedAtUtc = now;
    }

    /// <summary>True when the transition <paramref name="from"/> → <paramref name="to"/> is permitted.</summary>
    public static bool IsTransitionAllowed(UploadStatus from, UploadStatus to) =>
        AllowedTransitions.TryGetValue(from, out var allowed) && Array.IndexOf(allowed, to) >= 0;

    private void EnsureTransition(UploadStatus target)
    {
        if (!IsTransitionAllowed(Status, target))
        {
            throw new InvalidUploadStateTransitionException(Status, target);
        }
    }
}
