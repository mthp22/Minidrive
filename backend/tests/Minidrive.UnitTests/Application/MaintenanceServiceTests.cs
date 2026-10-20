using Minidrive.Application.Models;
using Minidrive.Domain.Enums;
using Minidrive.UnitTests.TestDoubles;
using Xunit;

namespace Minidrive.UnitTests.Application;

public sealed class MaintenanceServiceTests
{
    private const string UserA = "user-a";

    [Fact]
    public async Task ExpireStaleUploads_AbortsProviderUploadAndReleasesName()
    {
        var context = new ApplicationTestContext();
        var upload = await context.UploadService.InitiateAsync(
            UserA,
            new InitiateUploadRequest(null, "abandoned.bin", null, 25 * 1024 * 1024),
            default);

        context.Clock.Advance(TimeSpan.FromDays(2));

        var processed = await context.MaintenanceService.ExpireStaleUploadsAsync(batchSize: 10, default);

        Assert.Equal(1, processed);
        Assert.Equal(UploadStatus.Expired, context.Sessions.All.Single().Status);
        Assert.Empty(context.Nodes.All);
        Assert.Single(context.Storage.AbortedUploads);
        Assert.Equal(upload.UploadId, context.Sessions.All.Single().Id);
    }

    [Fact]
    public async Task ExpireStaleUploads_LeavesLiveSessionsAlone()
    {
        var context = new ApplicationTestContext();
        await context.UploadService.InitiateAsync(
            UserA,
            new InitiateUploadRequest(null, "active.bin", null, 25 * 1024 * 1024),
            default);

        var processed = await context.MaintenanceService.ExpireStaleUploadsAsync(batchSize: 10, default);

        Assert.Equal(0, processed);
        Assert.Equal(UploadStatus.Active, context.Sessions.All.Single().Status);
    }

    [Fact]
    public async Task ProcessStorageCleanup_DeletesObjectsAndDrainsTheQueue()
    {
        var context = new ApplicationTestContext();
        await context.CleanupQueue.EnqueueAsync(UserA, "users/a/files/1", default);
        await context.CleanupQueue.EnqueueAsync(UserA, "users/a/files/2", default);

        var processed = await context.MaintenanceService.ProcessStorageCleanupAsync(batchSize: 10, default);

        Assert.Equal(2, processed);
        Assert.Equal(2, context.Storage.DeletedObjects.Count);
        Assert.Empty(context.CleanupQueue.Items);
    }

    [Fact]
    public async Task ProcessStorageCleanup_WhenStorageFails_ReschedulesAndStops()
    {
        var context = new ApplicationTestContext();
        await context.CleanupQueue.EnqueueAsync(UserA, "users/a/files/1", default);
        context.Storage.DeleteException = new InvalidOperationException("storage down");

        var processed = await context.MaintenanceService.ProcessStorageCleanupAsync(batchSize: 10, default);

        Assert.Equal(0, processed);
        var item = Assert.Single(context.CleanupQueue.Items);
        Assert.Equal(1, item.Attempts);
    }

    [Fact]
    public async Task ProcessStorageCleanup_OnEmptyQueue_DoesNothing()
    {
        var context = new ApplicationTestContext();

        var processed = await context.MaintenanceService.ProcessStorageCleanupAsync(batchSize: 10, default);

        Assert.Equal(0, processed);
    }

    [Fact]
    public async Task PurgeExpiredTrash_WithRetentionElapsed_RemovesMetadataAndQueuesObjects()
    {
        var context = new ApplicationTestContext();
        var upload = await UploadAsync(context, "old.bin");
        await context.CommandService.DeleteNodeAsync(UserA, upload, default);

        // Object preserved on delete; nothing queued yet.
        Assert.Empty(context.CleanupQueue.Items);

        context.Clock.Advance(TimeSpan.FromDays(8)); // past the default 7-day retention

        var purged = await context.MaintenanceService.PurgeExpiredTrashAsync(batchSize: 10, default);

        Assert.Equal(1, purged);
        Assert.Empty(context.Nodes.All);
        var queued = Assert.Single(context.CleanupQueue.Items);
        Assert.Equal($"users/{UserA}/files/{upload}", queued.StorageKey);
    }

    [Fact]
    public async Task PurgeExpiredTrash_WithinRetention_LeavesNodeAlone()
    {
        var context = new ApplicationTestContext();
        var upload = await UploadAsync(context, "recent.bin");
        await context.CommandService.DeleteNodeAsync(UserA, upload, default);

        context.Clock.Advance(TimeSpan.FromDays(6)); // still inside the 7-day window

        var purged = await context.MaintenanceService.PurgeExpiredTrashAsync(batchSize: 10, default);

        Assert.Equal(0, purged);
        Assert.True(context.Nodes.All.Single(node => node.Id == upload).IsDeleted);
        Assert.Empty(context.CleanupQueue.Items);
    }

    [Fact]
    public async Task Restore_BeforeRetentionEnds_KeepsObjectAndRevivesNode()
    {
        var context = new ApplicationTestContext();
        var upload = await UploadAsync(context, "revive.bin");
        await context.CommandService.DeleteNodeAsync(UserA, upload, default);
        context.Clock.Advance(TimeSpan.FromDays(3));

        await context.CommandService.RestoreNodeAsync(UserA, upload, default);
        var purged = await context.MaintenanceService.PurgeExpiredTrashAsync(batchSize: 10, default);

        Assert.Equal(0, purged); // restored node is no longer deleted, so purge skips it
        Assert.False(context.Nodes.All.Single(node => node.Id == upload).IsDeleted);
    }

    private static async Task<string> UploadAsync(ApplicationTestContext context, string name)
    {
        var upload = await context.UploadService.InitiateAsync(
            UserA,
            new InitiateUploadRequest(null, name, "text/plain", 5),
            default);
        context.Storage.ObjectSizeBytes = 5;
        await context.UploadService.CompleteAsync(
            UserA,
            upload.UploadId,
            new CompleteUploadRequest(
                Enumerable.Range(1, upload.ExpectedPartCount)
                    .Select(number => new UploadedPartRequest(number, $"etag-{number}"))
                    .ToList()),
            default);
        return upload.FileId;
    }
}
