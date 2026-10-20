using Minidrive.Application.Abstractions.Persistence;
using Minidrive.Application.Common;
using Minidrive.Domain.Entities;
using Minidrive.Infrastructure.Persistence.Documents;
using Minidrive.Infrastructure.Persistence.Mapping;
using MongoDB.Driver;

namespace Minidrive.Infrastructure.Persistence;

/// <summary>
/// MongoDB implementation of <see cref="ITrashPurgeStore"/>. These queries are intentionally not
/// scoped to a user because retention is a cross-user maintenance concern; the store is only ever
/// resolved by the background maintenance worker.
/// </summary>
public sealed class MongoTrashPurgeStore : ITrashPurgeStore
{
    private static readonly FilterDefinitionBuilder<NodeDocument> Filters = Builders<NodeDocument>.Filter;
    private static readonly SortDefinitionBuilder<NodeDocument> Sorts = Builders<NodeDocument>.Sort;

    private readonly IMongoCollection<NodeDocument> _collection;

    public MongoTrashPurgeStore(IMongoDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _collection = database.GetCollection<NodeDocument>(MongoCollections.Nodes);
    }

    public async Task<IReadOnlyList<Node>> ListExpiredAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var filter = Filters.And(
            Filters.Eq(document => document.IsDeleted, true),
            Filters.Lte(document => document.DeletedAtUtc, BsonDate.ToBson(cutoff)));

        var documents = await _collection
            .Find(filter)
            .Sort(Sorts.Ascending(document => document.DeletedAtUtc))
            .Limit(batchSize)
            .ToListAsync(cancellationToken);

        return documents.Select(NodeMapper.ToDomain).ToList();
    }

    public async Task<long> DeleteManyAsync(IReadOnlyCollection<string> nodeIds, CancellationToken cancellationToken)
    {
        var valid = nodeIds.Where(IdFormat.IsValidObjectId).ToList();
        if (valid.Count == 0)
        {
            return 0;
        }

        var result = await _collection.DeleteManyAsync(Filters.In(document => document.Id, valid), cancellationToken);
        return result.DeletedCount;
    }
}
