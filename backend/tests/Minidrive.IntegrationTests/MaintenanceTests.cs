using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Minidrive.Application.Models;
using Minidrive.Application.Services;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace Minidrive.IntegrationTests;

/// <summary>
/// The workflows that bridge MongoDB and object storage: deferred deletion of soft-deleted nodes
/// and expiry of abandoned upload sessions. Both are invoked explicitly (the hosted worker is
/// disabled in tests) so the assertions are deterministic.
/// </summary>
public sealed class MaintenanceTests : ApiTestBase
{
    // Collection names are part of the persisted contract; they are asserted here on purpose so an
    // accidental rename surfaces as a failing test rather than silent data orphaning.
    private const string StorageCleanupsCollection = "storage_cleanups";
    private const string UploadSessionsCollection = "upload_sessions";

    public MaintenanceTests(IntegrationFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Deleting_a_file_keeps_object_restorable_until_retention_expires()
    {
        var client = CreateClient(NewUserId());

        var session = await InitiateAsync(client, $"deferred-{Guid.NewGuid():N}.bin");
        var complete = await PostJsonAsync(
            client,
            $"/api/v1/uploads/{session.UploadId}/complete",
            new { parts = new[] { new { partNumber = 1, eTag = "etag" } } });
        Assert.Equal(HttpStatusCode.OK, complete.StatusCode);

        var file = await ReadAsync<NodeDetails>(complete);
        Assert.NotNull(file);

        var storageKey = await GetStorageKeyAsync(file.Id);
        Assert.True(Factory.Storage.HasObject(storageKey), "The completed object must exist before deletion.");

        // Metadata first: the API must never block a delete on storage. The object is deliberately
        // preserved so the file stays restorable for the whole retention window.
        var delete = await client.DeleteAsync($"/api/v1/nodes/{file.Id}");
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.True(Factory.Storage.HasObject(storageKey), "The object must be preserved while the item is in the trash.");
        Assert.Equal(0, await CountCleanupItemsAsync(storageKey));

        // The deleted node is no longer visible through the normal node API...
        var gone = await client.GetAsync($"/api/v1/nodes/{file.Id}");
        Assert.Equal(HttpStatusCode.NotFound, gone.StatusCode);

        // ...but it is still listed in (and restorable from) the trash.
        var trashResponse = await client.GetAsync("/api/v1/trash");
        Assert.Equal(HttpStatusCode.OK, trashResponse.StatusCode);
        var trash = await ReadAsync<List<TrashItem>>(trashResponse);
        Assert.NotNull(trash);
        Assert.Contains(trash, item => item.Id == file.Id);

        // Age the deletion past the retention window, exactly as time passing would, then let the
        // retention purge retire the metadata and queue the object for removal.
        await AgeNodeDeletionAsync(file.Id, days: 8);
        var purged = await RunTrashPurgeAsync();
        Assert.True(purged >= 1, "The aged node should be claimed by the retention purge.");
        Assert.Equal(1, await CountCleanupItemsAsync(storageKey));

        var processed = await RunStorageCleanupAsync();
        Assert.True(processed >= 1, "At least the queued item should be processed.");

        Assert.False(Factory.Storage.HasObject(storageKey));
        Assert.Contains(Factory.Storage.DeletedKeys, key => key == storageKey);
    }

    [Fact]
    public async Task Abandoned_upload_sessions_are_expired_and_their_reserved_names_released()
    {
        var client = CreateClient(NewUserId());
        var fileName = $"stale-{Guid.NewGuid():N}.txt";

        var session = await InitiateAsync(client, fileName);

        // While the session lives, its pending node reserves the file name.
        var conflicting = await PostJsonAsync(
            client,
            "/api/v1/uploads/initiate",
            new { parentId = (string?)null, fileName, contentType = (string?)null, sizeBytes = 10L });
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);

        // Age the session past its deadline, exactly as time passing would.
        await ExpireSessionAsync(session.UploadId);

        var abortedBefore = Factory.Storage.AbortedUploads.Count;
        var expired = await RunExpirySweepAsync();
        Assert.True(expired >= 1, "The aged session should be claimed by the sweep.");

        var document = await GetSessionDocumentAsync(session.UploadId);
        Assert.Equal("Expired", document["Status"].AsString);
        Assert.Equal(abortedBefore + 1, Factory.Storage.AbortedUploads.Count);

        // The pending node is gone, so a new upload may take the name again.
        var retry = await PostJsonAsync(
            client,
            "/api/v1/uploads/initiate",
            new { parentId = (string?)null, fileName, contentType = (string?)null, sizeBytes = 10L });
        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);

        var pending = await client.GetAsync($"/api/v1/nodes/{session.FileId}");
        Assert.Equal(HttpStatusCode.NotFound, pending.StatusCode);
    }

    [Fact]
    public async Task Maintenance_passes_are_safe_to_run_when_there_is_nothing_to_do()
    {
        // Both operations are idempotent: an empty sweep/queue must complete without throwing.
        // (Counts are not asserted to be zero because other tests may legitimately leave work for
        // maintenance — for example a rejected completion that queued a cleanup.)
        _ = await RunExpirySweepAsync();
        _ = await RunStorageCleanupAsync();
    }

    private IMongoDatabase Database =>
        new MongoClient(Fixture.ConnectionString).GetDatabase(Factory.DatabaseName);

    private async Task<InitiateUploadResponse> InitiateAsync(HttpClient client, string fileName, long sizeBytes = 100)
    {
        var response = await PostJsonAsync(
            client,
            "/api/v1/uploads/initiate",
            new { parentId = (string?)null, fileName, contentType = (string?)null, sizeBytes });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var session = await ReadAsync<InitiateUploadResponse>(response);
        Assert.NotNull(session);
        return session;
    }

    private async Task<string> GetStorageKeyAsync(string nodeId)
    {
        // The storage key is server-side only (never returned by the API), so read it straight from
        // the persisted document.
        var nodes = Database.GetCollection<BsonDocument>("nodes");
        var node = await nodes.Find(new BsonDocument { { "_id", new ObjectId(nodeId) } }).FirstOrDefaultAsync();

        Assert.NotNull(node);
        Assert.False(node["StorageKey"].IsBsonNull, $"Node {nodeId} has no storage key.");
        return node["StorageKey"].AsString;
    }

    private async Task<long> CountCleanupItemsAsync(string storageKey)
    {
        var cleanups = Database.GetCollection<BsonDocument>(StorageCleanupsCollection);
        return await cleanups.CountDocumentsAsync(new BsonDocument { { "StorageKey", storageKey } });
    }

    private Task<int> RunStorageCleanupAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var maintenance = scope.ServiceProvider.GetRequiredService<MaintenanceService>();
        return maintenance.ProcessStorageCleanupAsync(100, CancellationToken.None);
    }

    private Task<int> RunExpirySweepAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var maintenance = scope.ServiceProvider.GetRequiredService<MaintenanceService>();
        return maintenance.ExpireStaleUploadsAsync(100, CancellationToken.None);
    }

    private Task<int> RunTrashPurgeAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var maintenance = scope.ServiceProvider.GetRequiredService<MaintenanceService>();
        return maintenance.PurgeExpiredTrashAsync(100, CancellationToken.None);
    }

    private async Task AgeNodeDeletionAsync(string nodeId, int days)
    {
        var nodes = Database.GetCollection<BsonDocument>("nodes");
        await nodes.UpdateOneAsync(
            new BsonDocument { { "_id", new ObjectId(nodeId) } },
            new BsonDocument
            {
                { "$set", new BsonDocument { { "DeletedAtUtc", DateTime.UtcNow.AddDays(-days) } } },
            });
    }

    private async Task ExpireSessionAsync(string uploadId)
    {
        var sessions = Database.GetCollection<BsonDocument>(UploadSessionsCollection);
        await sessions.UpdateOneAsync(
            new BsonDocument { { "_id", new ObjectId(uploadId) } },
            new BsonDocument { { "$set", new BsonDocument { { "ExpiresAtUtc", DateTime.UtcNow.AddHours(-1) } } } });
    }

    private async Task<BsonDocument> GetSessionDocumentAsync(string uploadId)
    {
        var sessions = Database.GetCollection<BsonDocument>(UploadSessionsCollection);
        var document = await sessions
            .Find(new BsonDocument { { "_id", new ObjectId(uploadId) } })
            .FirstOrDefaultAsync();

        Assert.NotNull(document);
        return document;
    }
}
