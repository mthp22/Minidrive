using Minidrive.Application.Abstractions.Persistence;
using Minidrive.Application.Common;
using Minidrive.Domain.Entities;
using Minidrive.Domain.Enums;
using Minidrive.Domain.Exceptions;
using Minidrive.Domain.ValueObjects;
using Minidrive.Infrastructure.Persistence.Documents;
using Minidrive.Infrastructure.Persistence.Mapping;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Minidrive.Infrastructure.Persistence;

/// <summary>
/// MongoDB implementation of <see cref="INodeRepository"/>. Every filter is scoped by user id so a
/// caller can never read or mutate another user's node by supplying a different identifier.
/// </summary>
public sealed class MongoNodeRepository : INodeRepository
{
    private static readonly FilterDefinitionBuilder<NodeDocument> Filters = Builders<NodeDocument>.Filter;
    private static readonly SortDefinitionBuilder<NodeDocument> Sorts = Builders<NodeDocument>.Sort;
    private static readonly UpdateDefinitionBuilder<NodeDocument> Updates = Builders<NodeDocument>.Update;

    private readonly IMongoCollection<NodeDocument> _collection;

    public MongoNodeRepository(IMongoDatabase database)
    {
        ArgumentNullException.ThrowIfNull(database);
        _collection = database.GetCollection<NodeDocument>(MongoCollections.Nodes);
    }

    public async Task InsertAsync(Node node, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);

        try
        {
            await _collection.InsertOneAsync(NodeMapper.ToDocument(node), cancellationToken: cancellationToken);
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new NameConflictException(node.Name);
        }
    }

    public async Task<Node?> FindAsync(
        string userId,
        string nodeId,
        bool includePending,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        if (!IdFormat.IsValidObjectId(nodeId))
        {
            return null;
        }

        var filter = Filters.And(Filters.Eq(document => document.Id, nodeId), Filters.Eq(document => document.UserId, userId));

        if (!includeDeleted)
        {
            filter = Filters.And(filter, Filters.Eq(document => document.IsDeleted, false));
        }

        if (!includePending)
        {
            filter = Filters.And(filter, Filters.Eq(document => document.Status, NodeStatus.Ready));
        }

        var document = await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        return document is null ? null : NodeMapper.ToDomain(document);
    }

    public async Task<IReadOnlyList<Node>> ListChildrenAsync(
        string userId,
        string? parentId,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var filter = Filters.And(
            Filters.Eq(document => document.UserId, userId),
            Filters.Eq(document => document.ParentId, parentId),
            Filters.Eq(document => document.IsDeleted, false),
            Filters.Eq(document => document.Status, NodeStatus.Ready));

        if (page.After is not null)
        {
            // Keyset pagination on the (Name, Id) sort key, which the unique sibling-name index backs.
            filter = Filters.And(
                filter,
                Filters.Or(
                    Filters.Gt(document => document.Name, page.After.Name),
                    Filters.And(
                        Filters.Eq(document => document.Name, page.After.Name),
                        Filters.Gt(document => document.Id, page.After.Id))));
        }

        var documents = await _collection
            .Find(filter)
            .Sort(Sorts.Ascending(document => document.Name).Ascending(document => document.Id))
            .Limit(page.PageSize)
            .ToListAsync(cancellationToken);

        return documents.Select(NodeMapper.ToDomain).ToList();
    }

    public async Task<bool> SiblingNameExistsAsync(
        string userId,
        string? parentId,
        string name,
        string? excludeNodeId,
        CancellationToken cancellationToken)
    {
        var filter = Filters.And(
            Filters.Eq(document => document.UserId, userId),
            Filters.Eq(document => document.ParentId, parentId),
            Filters.Eq(document => document.Name, name),
            Filters.Eq(document => document.IsDeleted, false));

        if (IdFormat.IsValidObjectId(excludeNodeId))
        {
            filter = Filters.And(filter, Filters.Ne(document => document.Id, excludeNodeId));
        }

        var count = await _collection.CountDocumentsAsync(filter, new CountOptions { Limit = 1 }, cancellationToken);
        return count > 0;
    }

    public async Task<bool> UpdateAsync(Node node, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (!IdFormat.IsValidObjectId(node.Id))
        {
            return false;
        }

        var filter = Filters.And(Filters.Eq(document => document.Id, node.Id), Filters.Eq(document => document.UserId, node.UserId));

        try
        {
            var result = await _collection.ReplaceOneAsync(filter, NodeMapper.ToDocument(node), cancellationToken: cancellationToken);
            return result.MatchedCount > 0;
        }
        catch (MongoWriteException exception) when (exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new NameConflictException(node.Name);
        }
    }

    public async Task<long> RebaseDescendantPathsAsync(
        string userId,
        string oldPrefix,
        string newPrefix,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var filter = Filters.And(
            Filters.Eq(document => document.UserId, userId),
            Filters.Regex(document => document.MaterializedPath, PrefixRegex(oldPrefix)));

        // A single bulk update rewrites every descendant path without walking the tree. A retry after
        // a partial failure finds no documents still matching the old prefix, so it is idempotent.
        var pipeline = PipelineDefinition<NodeDocument, NodeDocument>.Create(
        [
            new BsonDocument("$set", new BsonDocument
            {
                {
                    "MaterializedPath",
                    new BsonDocument("$concat", new BsonArray
                    {
                        newPrefix,
                        new BsonDocument("$substrCP", new BsonArray
                        {
                            "$MaterializedPath",
                            oldPrefix.Length,
                            MaterializedPath.MaxLength,
                        }),
                    })
                },
                { "UpdatedAtUtc", BsonValue.Create(BsonDate.ToBson(now)) },
            }),
        ]);

        var result = await _collection.UpdateManyAsync(filter, pipeline, cancellationToken: cancellationToken);
        return result.ModifiedCount;
    }

    public async Task<IReadOnlyList<Node>> ListDescendantsAsync(
        string userId,
        string descendantPrefix,
        CancellationToken cancellationToken)
    {
        var filter = Filters.And(
            Filters.Eq(document => document.UserId, userId),
            Filters.Eq(document => document.IsDeleted, false),
            Filters.Regex(document => document.MaterializedPath, PrefixRegex(descendantPrefix)));

        var documents = await _collection.Find(filter).ToListAsync(cancellationToken);
        return documents.Select(NodeMapper.ToDomain).ToList();
    }

    public async Task<long> SoftDeleteSubtreeAsync(
        string userId,
        string selfNodeId,
        string descendantPrefix,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var filter = Filters.And(
            Filters.Eq(document => document.UserId, userId),
            Filters.Eq(document => document.IsDeleted, false),
            Filters.Or(
                Filters.Eq(document => document.Id, selfNodeId),
                Filters.Regex(document => document.MaterializedPath, PrefixRegex(descendantPrefix))));

        var update = Updates
            .Set(document => document.IsDeleted, true)
            .Set(document => document.DeletedAtUtc, BsonDate.ToBson(now))
            .Set(document => document.UpdatedAtUtc, BsonDate.ToBson(now));

        var result = await _collection.UpdateManyAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount;
    }

    public async Task<bool> TryMarkReadyAsync(
        string userId,
        string nodeId,
        long sizeBytes,
        string? eTag,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!IdFormat.IsValidObjectId(nodeId))
        {
            return false;
        }

        var filter = Filters.And(
            Filters.Eq(document => document.Id, nodeId),
            Filters.Eq(document => document.UserId, userId),
            Filters.Eq(document => document.Status, NodeStatus.Pending));

        var update = Updates
            .Set(document => document.Status, NodeStatus.Ready)
            .Set(document => document.SizeBytes, sizeBytes)
            .Set(document => document.ETag, eTag)
            .Set(document => document.UpdatedAtUtc, BsonDate.ToBson(now));

        var result = await _collection.UpdateOneAsync(filter, update, cancellationToken: cancellationToken);
        return result.ModifiedCount > 0;
    }

    public async Task<long> SumDescendantFileSizeAsync(
        string userId,
        string descendantPrefix,
        CancellationToken cancellationToken)
    {
        var group = new BsonDocument
        {
            { "_id", BsonNull.Value },
            { "total", new BsonDocument("$sum", "$SizeBytes") },
        };

        var results = await _collection
            .Aggregate()
            .Match(FileFilter(userId, descendantPrefix))
            .Group(group)
            .ToListAsync(cancellationToken);

        return results.Count == 0 ? 0 : results[0]["total"].ToInt64();
    }

    public async Task<long> SumUserFileSizeAsync(string userId, CancellationToken cancellationToken)
    {
        var filter = Filters.And(
            Filters.Eq(document => document.UserId, userId),
            Filters.Eq(document => document.IsDeleted, false),
            Filters.Eq(document => document.Type, NodeType.File),
            Filters.Eq(document => document.Status, NodeStatus.Ready));

        var group = new BsonDocument
        {
            { "_id", BsonNull.Value },
            { "total", new BsonDocument("$sum", "$SizeBytes") },
        };

        var results = await _collection
            .Aggregate()
            .Match(filter)
            .Group(group)
            .ToListAsync(cancellationToken);

        return results.Count == 0 ? 0 : results[0]["total"].ToInt64();
    }

    public async Task<IReadOnlyList<string>> ListDescendantFileStorageKeysAsync(
        string userId,
        string descendantPrefix,
        CancellationToken cancellationToken)
    {
        var keys = await _collection
            .Find(FileFilter(userId, descendantPrefix))
            .Project(document => document.StorageKey)
            .ToListAsync(cancellationToken);

        return keys
            .Where(key => !string.IsNullOrEmpty(key))
            .Select(key => key!)
            .ToList();
    }

    public async Task<bool> DeleteAsync(string userId, string nodeId, CancellationToken cancellationToken)
    {
        if (!IdFormat.IsValidObjectId(nodeId))
        {
            return false;
        }

        var filter = Filters.And(Filters.Eq(document => document.Id, nodeId), Filters.Eq(document => document.UserId, userId));
        var result = await _collection.DeleteOneAsync(filter, cancellationToken);
        return result.DeletedCount > 0;
    }

    private static FilterDefinition<NodeDocument> FileFilter(string userId, string descendantPrefix) =>
        Filters.And(
            Filters.Eq(document => document.UserId, userId),
            Filters.Eq(document => document.IsDeleted, false),
            Filters.Eq(document => document.Type, NodeType.File),
            Filters.Eq(document => document.Status, NodeStatus.Ready),
            Filters.Regex(document => document.MaterializedPath, PrefixRegex(descendantPrefix)));

    private static BsonRegularExpression PrefixRegex(string prefix) =>
        new(MaterializedPath.ToAnchoredRegexPattern(prefix));
}
