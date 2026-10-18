namespace Minidrive.Application.Abstractions.Persistence;

/// <summary>
/// A deferred request to remove an object from storage. Rows are written when a node is deleted
/// so that a storage outage cannot silently leak objects, and are retried by maintenance.
/// </summary>
/// <param name="Id">Cleanup row identifier.</param>
/// <param name="UserId">Owner of the object.</param>
/// <param name="StorageKey">Opaque object key to remove.</param>
/// <param name="Attempts">Number of failed attempts so far.</param>
public sealed record StorageCleanupItem(string Id, string UserId, string StorageKey, int Attempts);

/// <summary>
/// Durable queue of storage deletions. MongoDB is the source of truth for metadata, so cleanup is
/// decoupled from the request that triggered it and can be replayed safely.
/// </summary>
public interface IStorageCleanupQueue
{
    Task EnqueueAsync(string userId, string storageKey, CancellationToken cancellationToken);

    /// <summary>Atomically claims the oldest pending item for processing, or returns null.</summary>
    Task<StorageCleanupItem?> DequeueAsync(CancellationToken cancellationToken);

    /// <summary>Removes an item once its object has been deleted.</summary>
    Task RemoveAsync(string itemId, CancellationToken cancellationToken);

    /// <summary>Records a failed attempt and makes the item eligible for a later retry.</summary>
    Task RescheduleAsync(string itemId, string reason, DateTimeOffset now, CancellationToken cancellationToken);
}
