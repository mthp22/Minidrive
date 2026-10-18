using Minidrive.Application.Abstractions.Persistence;
using Minidrive.Application.Common;
using Minidrive.Infrastructure.Persistence.Documents;
using Minidrive.Infrastructure.Persistence.Mapping;
using MongoDB.Driver;

namespace Minidrive.Infrastructure.Persistence;

/// <summary>
/// MongoDB-backed <see cref="IStorageCleanupQueue"/>. Items are claimed atomically with
/// <c>findAndModify</c> so two maintenance runs cannot delete the same object concurrently.
/// </summary>
public sealed class MongoStorageCleanupQueue : IStorageCleanupQueue
{
    private static readonly FilterDefinitionBuilder<StorageCleanupDocument> Filters = Builders<StorageCleanupDocument>.Filter;
    private static readonly SortDefinitionBuilder<StorageCleanupDocument> Sorts = Builders<StorageCleanupDocument>.Sort;
    private static readonly UpdateDefinitionBuilder<StorageCleanupDocument> Updates = Builders<StorageCleanupDocument>.Update;

    private readonly IMongoCollection<StorageCleanupDocument> _collection;
    private readonly TimeProvider _clock;

    public MongoStorageCleanupQueue(IMongoDatabase database, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(clock);

        _collection = database.GetCollection<StorageCleanupDocument>(MongoCollections.StorageCleanups);
        _clock = clock;
    }

    public async Task EnqueueAsync(string userId, string storageKey, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        var document = new StorageCleanupDocument
        {
            Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString(),
            UserId = userId,
            StorageKey = storageKey,
            Attempts = 0,
            CreatedAtUtc = BsonDate.ToBson(_clock.GetUtcNow()),
            ClaimedAtUtc = null,
            LastError = null,
        };

        await _collection.InsertOneAsync(document, cancellationToken: cancellationToken);
    }

    public async Task<StorageCleanupItem?> DequeueAsync(CancellationToken cancellationToken)
    {
        var filter = Filters.Eq(document => document.ClaimedAtUtc, (DateTime?)null);
        var update = Updates.Set(document => document.ClaimedAtUtc, BsonDate.ToBson(_clock.GetUtcNow()));
        var options = new FindOneAndUpdateOptions<StorageCleanupDocument>
        {
            ReturnDocument = ReturnDocument.After,
            Sort = Sorts.Ascending(document => document.CreatedAtUtc),
        };

        var document = await _collection.FindOneAndUpdateAsync(filter, update, options, cancellationToken);
        return document is null ? null : new StorageCleanupItem(document.Id, document.UserId, document.StorageKey, document.Attempts);
    }

    public async Task RemoveAsync(string itemId, CancellationToken cancellationToken)
    {
        if (!IdFormat.IsValidObjectId(itemId))
        {
            return;
        }

        await _collection.DeleteOneAsync(Filters.Eq(document => document.Id, itemId), cancellationToken);
    }

    public async Task RescheduleAsync(string itemId, string reason, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!IdFormat.IsValidObjectId(itemId))
        {
            return;
        }

        var update = Updates
            .Set(document => document.ClaimedAtUtc, (DateTime?)null)
            .Set(document => document.LastError, reason)
            .Inc(document => document.Attempts, 1);

        await _collection.UpdateOneAsync(Filters.Eq(document => document.Id, itemId), update, cancellationToken: cancellationToken);
    }
}
