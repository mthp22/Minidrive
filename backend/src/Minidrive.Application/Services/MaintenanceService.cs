using Minidrive.Application.Abstractions.Persistence;
using Minidrive.Application.Abstractions.Storage;
using Minidrive.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Minidrive.Application.Services;

/// <summary>
/// Recovery and housekeeping for workflows that span MongoDB and object storage.
/// </summary>
/// <remarks>
/// There is no background worker assumption: the API exposes these as an explicit, callable
/// service so an operator, a scheduled job or a hosted service can invoke them. Both operations are
/// idempotent and safe to run repeatedly.
/// </remarks>
public sealed class MaintenanceService
{
    private readonly IUploadSessionRepository _sessions;
    private readonly INodeRepository _nodes;
    private readonly IObjectStorageService _storage;
    private readonly IStorageCleanupQueue _cleanupQueue;
    private readonly TimeProvider _clock;
    private readonly ILogger<MaintenanceService> _logger;

    public MaintenanceService(
        IUploadSessionRepository sessions,
        INodeRepository nodes,
        IObjectStorageService storage,
        IStorageCleanupQueue cleanupQueue,
        TimeProvider clock,
        ILogger<MaintenanceService> logger)
    {
        _sessions = sessions;
        _nodes = nodes;
        _storage = storage;
        _cleanupQueue = cleanupQueue;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Aborts and expires upload sessions whose lifetime has passed, discarding abandoned
    /// multipart uploads and releasing the reserved file names.
    /// </summary>
    public async Task<int> ExpireStaleUploadsAsync(int batchSize, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var expired = await _sessions.ListExpiredAsync(now, batchSize, cancellationToken);
        var processed = 0;

        foreach (var session in expired)
        {
            // Claim the session before touching storage so a concurrently completing request wins.
            var claimed = await _sessions.TransitionAsync(
                new UploadSessionTransition(session.Id, session.UserId, session.Status, UploadStatus.Aborting),
                now,
                cancellationToken);

            if (claimed is null)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(session.ProviderUploadId))
            {
                try
                {
                    await _storage.AbortMultipartUploadAsync(session.StorageKey, session.ProviderUploadId, cancellationToken);
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(
                        exception,
                        "Failed to abort the provider upload while expiring session {UploadSessionId}.",
                        session.Id);
                }
            }

            try
            {
                await _nodes.DeleteAsync(session.UserId, session.FileNodeId, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "Failed to remove the pending node while expiring session {UploadSessionId}.",
                    session.Id);
            }

            await _sessions.TransitionAsync(
                new UploadSessionTransition(
                    session.Id,
                    session.UserId,
                    UploadStatus.Aborting,
                    UploadStatus.Expired,
                    CompletedAtUtc: now),
                now,
                cancellationToken);

            processed++;
        }

        if (processed > 0)
        {
            _logger.LogInformation("Expired {Count} stale upload session(s).", processed);
        }

        return processed;
    }

    /// <summary>
    /// Deletes objects left behind by node deletion. Items are claimed one at a time and rescheduled
    /// on failure so a storage outage never discards work.
    /// </summary>
    public async Task<int> ProcessStorageCleanupAsync(int batchSize, CancellationToken cancellationToken)
    {
        var processed = 0;

        for (var index = 0; index < batchSize; index++)
        {
            var item = await _cleanupQueue.DequeueAsync(cancellationToken);
            if (item is null)
            {
                break;
            }

            try
            {
                await _storage.DeleteObjectAsync(item.StorageKey, cancellationToken);
                await _cleanupQueue.RemoveAsync(item.Id, cancellationToken);
                processed++;
            }
            catch (Exception exception)
            {
                // Stop the batch: storage is likely unavailable, and retrying the same item
                // immediately would only add load.
                await _cleanupQueue.RescheduleAsync(item.Id, exception.GetType().Name, _clock.GetUtcNow(), cancellationToken);
                _logger.LogWarning(
                    exception,
                    "Storage cleanup failed for object owned by user {UserId} (attempt {Attempts}).",
                    item.UserId,
                    item.Attempts + 1);
                break;
            }
        }

        return processed;
    }
}
