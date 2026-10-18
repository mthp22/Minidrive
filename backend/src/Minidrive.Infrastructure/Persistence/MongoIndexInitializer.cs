using Minidrive.Infrastructure.Persistence.Documents;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Minidrive.Infrastructure.Persistence;

/// <summary>Creates the indexes the drive relies on. Safe to run repeatedly.</summary>
public interface IMongoIndexInitializer
{
    Task EnsureIndexesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Creates every required MongoDB index programmatically so a new deployment is correct without a
/// manual migration step.
/// </summary>
/// <remarks>
/// Index creation is idempotent: creating an index that already exists with the same definition is
/// a no-op. The unique indexes will fail if pre-existing data already violates them, which is the
/// intended behaviour — surfacing the data problem instead of silently skipping the constraint.
/// </remarks>
public sealed class MongoIndexInitializer : IMongoIndexInitializer
{
    private readonly IMongoDatabase _database;
    private readonly ILogger<MongoIndexInitializer> _logger;

    public MongoIndexInitializer(IMongoDatabase database, ILogger<MongoIndexInitializer> logger)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(logger);

        _database = database;
        _logger = logger;
    }

    public async Task EnsureIndexesAsync(CancellationToken cancellationToken)
    {
        await EnsureNodeIndexesAsync(cancellationToken);
        await EnsureUploadSessionIndexesAsync(cancellationToken);
        await EnsureStorageCleanupIndexesAsync(cancellationToken);

        _logger.LogInformation("MongoDB indexes are initialised.");
    }

    private async Task EnsureNodeIndexesAsync(CancellationToken cancellationToken)
    {
        var collection = _database.GetCollection<NodeDocument>(MongoCollections.Nodes);

        var models = new List<CreateIndexModel<NodeDocument>>
        {
            // Listing a folder's direct children is the most frequent read.
            new(
                Builders<NodeDocument>.IndexKeys.Ascending(document => document.UserId).Ascending(document => document.ParentId),
                new CreateIndexOptions { Name = "ix_nodes_user_parent" }),

            // Descendant operations (folder size, subtree delete, move rebase) filter on an anchored
            // prefix of the materialized path, which this index serves.
            new(
                Builders<NodeDocument>.IndexKeys.Ascending(document => document.UserId).Ascending(document => document.MaterializedPath),
                new CreateIndexOptions { Name = "ix_nodes_user_path" }),

            // A sibling name must be unique within a folder while the node is live. The partial filter
            // releases the name as soon as a node is soft-deleted.
            new(
                Builders<NodeDocument>.IndexKeys
                    .Ascending(document => document.UserId)
                    .Ascending(document => document.ParentId)
                    .Ascending(document => document.Name),
                new CreateIndexOptions<NodeDocument>
                {
                    Name = "ux_nodes_user_parent_name",
                    Unique = true,
                    PartialFilterExpression = Builders<NodeDocument>.Filter.Eq(document => document.IsDeleted, false),
                }),

            // Per-user quota/usage aggregation over ready files.
            new(
                Builders<NodeDocument>.IndexKeys
                    .Ascending(document => document.UserId)
                    .Ascending(document => document.IsDeleted)
                    .Ascending(document => document.Type)
                    .Ascending(document => document.Status),
                new CreateIndexOptions { Name = "ix_nodes_usage" }),

            // Storage keys are unique. Only files have one, so the index is partial on string values.
            new(
                Builders<NodeDocument>.IndexKeys.Ascending(document => document.StorageKey),
                new CreateIndexOptions<NodeDocument>
                {
                    Name = "ux_nodes_storage_key",
                    Unique = true,
                    PartialFilterExpression = Builders<NodeDocument>.Filter.Type(document => document.StorageKey, BsonType.String),
                }),
        };

        await collection.Indexes.CreateManyAsync(models, cancellationToken);
    }

    private async Task EnsureUploadSessionIndexesAsync(CancellationToken cancellationToken)
    {
        var collection = _database.GetCollection<UploadSessionDocument>(MongoCollections.UploadSessions);

        var models = new List<CreateIndexModel<UploadSessionDocument>>
        {
            // Drives the expiry sweep that looks for open sessions past their expiry timestamp.
            new(
                Builders<UploadSessionDocument>.IndexKeys
                    .Ascending(document => document.Status)
                    .Ascending(document => document.ExpiresAtUtc),
                new CreateIndexOptions { Name = "ix_upload_sessions_status_expiry" }),

            // Supports per-user session lookups and operational diagnostics.
            new(
                Builders<UploadSessionDocument>.IndexKeys
                    .Ascending(document => document.UserId)
                    .Ascending(document => document.Status),
                new CreateIndexOptions { Name = "ix_upload_sessions_user_status" }),
        };

        // Note: there is deliberately no TTL index on ExpiresAtUtc. Sessions must transition to
        // Expired so that abandoned multipart uploads are aborted and pending names are released;
        // letting MongoDB delete the documents would leak those resources.
        await collection.Indexes.CreateManyAsync(models, cancellationToken);
    }

    private async Task EnsureStorageCleanupIndexesAsync(CancellationToken cancellationToken)
    {
        var collection = _database.GetCollection<StorageCleanupDocument>(MongoCollections.StorageCleanups);

        var models = new List<CreateIndexModel<StorageCleanupDocument>>
        {
            // Serves the atomic claim of the oldest unclaimed cleanup item.
            new(
                Builders<StorageCleanupDocument>.IndexKeys
                    .Ascending(document => document.ClaimedAtUtc)
                    .Ascending(document => document.CreatedAtUtc),
                new CreateIndexOptions { Name = "ix_storage_cleanups_claim_created" }),
        };

        await collection.Indexes.CreateManyAsync(models, cancellationToken);
    }
}
