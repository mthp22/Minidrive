using Minidrive.Application.Abstractions.Persistence;

namespace Minidrive.UnitTests.TestDoubles;

/// <summary>In-memory storage-cleanup queue used to verify deferred object deletion.</summary>
public sealed class InMemoryStorageCleanupQueue : IStorageCleanupQueue
{
    private readonly List<StorageCleanupItem> _items = [];
    private int _counter;

    public IReadOnlyList<StorageCleanupItem> Items => _items;

    public Task EnqueueAsync(string userId, string storageKey, CancellationToken cancellationToken)
    {
        _items.Add(new StorageCleanupItem((++_counter).ToString("x24"), userId, storageKey, 0));
        return Task.CompletedTask;
    }

    public Task<StorageCleanupItem?> DequeueAsync(CancellationToken cancellationToken) =>
        Task.FromResult(_items.Count == 0 ? null : _items[0]);

    public Task RemoveAsync(string itemId, CancellationToken cancellationToken)
    {
        _items.RemoveAll(item => item.Id == itemId);
        return Task.CompletedTask;
    }

    public Task RescheduleAsync(string itemId, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var index = _items.FindIndex(item => item.Id == itemId);
        if (index >= 0)
        {
            _items[index] = _items[index] with { Attempts = _items[index].Attempts + 1 };
        }

        return Task.CompletedTask;
    }
}
