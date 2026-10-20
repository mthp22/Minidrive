using Minidrive.Application.Exceptions;
using Minidrive.Application.Models;
using Minidrive.Domain.Exceptions;
using Minidrive.UnitTests.TestDoubles;
using Xunit;

namespace Minidrive.UnitTests.Application;

public sealed class NodeCommandServiceTests
{
    private const string UserA = "user-a";
    private const string UserB = "user-b";

    [Fact]
    public async Task CreateFolder_AtRoot_IsReadyAndUnaffectedByDeletion()
    {
        var context = new ApplicationTestContext();

        var folder = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Photos"), default);

        Assert.Equal("Photos", folder.Name);
        Assert.Null(folder.ParentId);
        Assert.Equal(Minidrive.Domain.Enums.NodeType.Folder, folder.Type);
    }

    [Fact]
    public async Task CreateFolder_UsesParentPathForMaterializedPath()
    {
        var context = new ApplicationTestContext();
        var parent = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Parent"), default);

        var child = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(parent.Id, "Child"), default);

        var stored = context.Nodes.All.Single(node => node.Id == child.Id);
        Assert.Equal($",{parent.Id},", stored.MaterializedPath);
    }

    [Fact]
    public async Task CreateFolder_DuplicateName_Throws()
    {
        var context = new ApplicationTestContext();
        await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Photos"), default);

        await Assert.ThrowsAsync<NameConflictException>(() =>
            context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Photos"), default));
    }

    [Fact]
    public async Task CreateFolder_OtherUsersCanReuseTheSameName()
    {
        var context = new ApplicationTestContext();
        await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Photos"), default);

        var other = await context.CommandService.CreateFolderAsync(UserB, new CreateFolderRequest(null, "Photos"), default);

        Assert.Equal("Photos", other.Name);
    }

    [Fact]
    public async Task CreateFolder_MissingParent_ThrowsNotFound()
    {
        var context = new ApplicationTestContext();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest("0000000000000000000000ff", "Child"), default));
    }

    [Fact]
    public async Task CreateFolder_IntoAnotherUsersFolder_ThrowsNotFound()
    {
        var context = new ApplicationTestContext();
        var folder = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Private"), default);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            context.CommandService.CreateFolderAsync(UserB, new CreateFolderRequest(folder.Id, "Intruder"), default));
    }

    [Fact]
    public async Task Rename_ChangesTheName()
    {
        var context = new ApplicationTestContext();
        var folder = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Photos"), default);

        var renamed = await context.CommandService.RenameNodeAsync(UserA, folder.Id, new RenameNodeRequest("Pictures"), default);

        Assert.Equal("Pictures", renamed.Name);
    }

    [Fact]
    public async Task Rename_TrimsAndRejectsInvalidNames()
    {
        var context = new ApplicationTestContext();
        var folder = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Photos"), default);

        await Assert.ThrowsAsync<InvalidNodeNameException>(() =>
            context.CommandService.RenameNodeAsync(UserA, folder.Id, new RenameNodeRequest("bad/name"), default));
    }

    [Fact]
    public async Task Rename_ToExistingSiblingName_Throws()
    {
        var context = new ApplicationTestContext();
        await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "One"), default);
        var second = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Two"), default);

        await Assert.ThrowsAsync<NameConflictException>(() =>
            context.CommandService.RenameNodeAsync(UserA, second.Id, new RenameNodeRequest("One"), default));
    }

    [Fact]
    public async Task Rename_OfAnotherUsersNode_ThrowsNotFound()
    {
        var context = new ApplicationTestContext();
        var folder = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Photos"), default);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            context.CommandService.RenameNodeAsync(UserB, folder.Id, new RenameNodeRequest("Stolen"), default));
    }

    [Fact]
    public async Task Move_IntoItself_Throws()
    {
        var context = new ApplicationTestContext();
        var folder = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Photos"), default);

        await Assert.ThrowsAsync<InvalidMoveException>(() =>
            context.CommandService.MoveNodeAsync(UserA, folder.Id, new MoveNodeRequest(folder.Id), default));
    }

    [Fact]
    public async Task Move_IntoDescendant_Throws()
    {
        var context = new ApplicationTestContext();
        var parent = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Parent"), default);
        var child = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(parent.Id, "Child"), default);

        await Assert.ThrowsAsync<InvalidMoveException>(() =>
            context.CommandService.MoveNodeAsync(UserA, parent.Id, new MoveNodeRequest(child.Id), default));
    }

    [Fact]
    public async Task Move_IntoMissingFolder_ThrowsNotFound()
    {
        var context = new ApplicationTestContext();
        var folder = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Photos"), default);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            context.CommandService.MoveNodeAsync(UserA, folder.Id, new MoveNodeRequest("0000000000000000000000aa"), default));
    }

    [Fact]
    public async Task Move_RebasesEveryDescendantPath()
    {
        var context = new ApplicationTestContext();
        var outer = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Outer"), default);
        var inner = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(outer.Id, "Inner"), default);
        var leaf = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(inner.Id, "Leaf"), default);

        // Move "Outer" to the root of... itself is already root, so move "Inner" to the root instead.
        await context.CommandService.MoveNodeAsync(UserA, inner.Id, new MoveNodeRequest(null), default);

        var storedInner = context.Nodes.All.Single(node => node.Id == inner.Id);
        var storedLeaf = context.Nodes.All.Single(node => node.Id == leaf.Id);

        Assert.Equal(",", storedInner.MaterializedPath);
        Assert.Equal($",{inner.Id},", storedLeaf.MaterializedPath);
    }

    [Fact]
    public async Task Move_MergesIntoNewParentAndRejectsNameClash()
    {
        var context = new ApplicationTestContext();
        var target = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Target"), default);
        await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(target.Id, "Report"), default);
        var moving = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Report"), default);

        await Assert.ThrowsAsync<NameConflictException>(() =>
            context.CommandService.MoveNodeAsync(UserA, moving.Id, new MoveNodeRequest(target.Id), default));
    }

    [Fact]
    public async Task Delete_File_SoftDeletesAndPreservesObjectForRestore()
    {
        var context = new ApplicationTestContext();
        var upload = await context.UploadService.InitiateAsync(
            UserA,
            new InitiateUploadRequest(null, "notes.txt", "text/plain", 12 * 1024 * 1024),
            default);
        context.Storage.ObjectSizeBytes = 12 * 1024 * 1024;
        await context.UploadService.CompleteAsync(UserA, upload.UploadId, CompleteParts(upload.ExpectedPartCount), default);

        await context.CommandService.DeleteNodeAsync(UserA, upload.FileId, default);

        var stored = context.Nodes.All.Single(node => node.Id == upload.FileId);
        Assert.True(stored.IsDeleted);
        Assert.NotNull(stored.DeletedAtUtc);

        // Storage objects are preserved so the file stays restorable for the whole retention window.
        Assert.Empty(context.CleanupQueue.Items);
    }

    [Fact]
    public async Task Restore_AfterDelete_BringsFileBackIntoListing()
    {
        var context = new ApplicationTestContext();
        var upload = await context.UploadService.InitiateAsync(
            UserA,
            new InitiateUploadRequest(null, "notes.txt", "text/plain", 12 * 1024 * 1024),
            default);
        context.Storage.ObjectSizeBytes = 12 * 1024 * 1024;
        await context.UploadService.CompleteAsync(UserA, upload.UploadId, CompleteParts(upload.ExpectedPartCount), default);

        await context.CommandService.DeleteNodeAsync(UserA, upload.FileId, default);
        Assert.Empty((await context.QueryService.ListChildrenAsync(UserA, null, null, null, default)).Items);

        var restored = await context.CommandService.RestoreNodeAsync(UserA, upload.FileId, default);

        Assert.Equal("notes.txt", restored.Name);
        var stored = context.Nodes.All.Single(node => node.Id == upload.FileId);
        Assert.False(stored.IsDeleted);
        Assert.Null(stored.DeletedAtUtc);
        var page = await context.QueryService.ListChildrenAsync(UserA, null, null, null, default);
        Assert.Contains(page.Items, node => node.Id == upload.FileId);
    }

    [Fact]
    public async Task Restore_OnLiveNode_IsIdempotent()
    {
        var context = new ApplicationTestContext();
        var upload = await context.UploadService.InitiateAsync(
            UserA,
            new InitiateUploadRequest(null, "live.txt", "text/plain", 5),
            default);
        context.Storage.ObjectSizeBytes = 5;
        await context.UploadService.CompleteAsync(UserA, upload.UploadId, CompleteParts(upload.ExpectedPartCount), default);

        var details = await context.CommandService.RestoreNodeAsync(UserA, upload.FileId, default);

        Assert.Equal("live.txt", details.Name);
        Assert.False(context.Nodes.All.Single(node => node.Id == upload.FileId).IsDeleted);
    }

    [Fact]
    public async Task Purge_AfterDelete_RemovesMetadataAndQueuesObject()
    {
        var context = new ApplicationTestContext();
        var upload = await context.UploadService.InitiateAsync(
            UserA,
            new InitiateUploadRequest(null, "notes.txt", "text/plain", 12 * 1024 * 1024),
            default);
        context.Storage.ObjectSizeBytes = 12 * 1024 * 1024;
        await context.UploadService.CompleteAsync(UserA, upload.UploadId, CompleteParts(upload.ExpectedPartCount), default);

        await context.CommandService.DeleteNodeAsync(UserA, upload.FileId, default);
        await context.CommandService.PurgeNodeAsync(UserA, upload.FileId, default);

        Assert.DoesNotContain(context.Nodes.All, node => node.Id == upload.FileId);
        var queued = Assert.Single(context.CleanupQueue.Items);
        Assert.Equal($"users/{UserA}/files/{upload.FileId}", queued.StorageKey);
    }

    [Fact]
    public async Task EmptyTrash_RemovesEveryDeletedNodeAndQueuesObjects()
    {
        var context = new ApplicationTestContext();
        var first = await context.UploadService.InitiateAsync(
            UserA,
            new InitiateUploadRequest(null, "a.txt", "text/plain", 5),
            default);
        context.Storage.ObjectSizeBytes = 5;
        await context.UploadService.CompleteAsync(UserA, first.UploadId, CompleteParts(first.ExpectedPartCount), default);

        var second = await context.UploadService.InitiateAsync(
            UserA,
            new InitiateUploadRequest(null, "b.txt", "text/plain", 5),
            default);
        await context.UploadService.CompleteAsync(UserA, second.UploadId, CompleteParts(second.ExpectedPartCount), default);

        await context.CommandService.DeleteNodeAsync(UserA, first.FileId, default);
        await context.CommandService.DeleteNodeAsync(UserA, second.FileId, default);

        var removed = await context.CommandService.EmptyTrashAsync(UserA, default);

        Assert.Equal(2, removed);
        Assert.Empty(context.Nodes.All);
        Assert.Equal(2, context.CleanupQueue.Items.Count);
    }

    [Fact]
    public async Task Delete_Folder_SoftDeletesWholeSubtreeAndPreservesObjects()
    {
        var context = new ApplicationTestContext();
        var folder = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Docs"), default);

        var first = await InitiateAndCompleteAsync(context, UserA, folder.Id, "a.txt");
        var second = await InitiateAndCompleteAsync(context, UserA, folder.Id, "b.txt");

        await context.CommandService.DeleteNodeAsync(UserA, folder.Id, default);

        Assert.True(context.Nodes.All.Single(node => node.Id == folder.Id).IsDeleted);
        Assert.True(context.Nodes.All.Single(node => node.Id == first).IsDeleted);
        Assert.True(context.Nodes.All.Single(node => node.Id == second).IsDeleted);

        // Objects are kept so the whole subtree stays restorable.
        Assert.Empty(context.CleanupQueue.Items);

        // Restoring the folder restores the whole subtree and makes files visible again.
        await context.CommandService.RestoreNodeAsync(UserA, folder.Id, default);
        Assert.False(context.Nodes.All.Single(node => node.Id == folder.Id).IsDeleted);
        Assert.False(context.Nodes.All.Single(node => node.Id == first).IsDeleted);
        Assert.False(context.Nodes.All.Single(node => node.Id == second).IsDeleted);
        var children = await context.QueryService.ListChildrenAsync(UserA, folder.Id, null, null, default);
        Assert.Equal(2, children.Items.Count);
    }

    [Fact]
    public async Task Trash_Listing_ShowsOnlyTopLevelDeletedItems()
    {
        var context = new ApplicationTestContext();
        var folder = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Docs"), default);
        var child = await InitiateAndCompleteAsync(context, UserA, folder.Id, "child.txt");
        var lone = await InitiateAndCompleteAsync(context, UserA, null, "lone.txt");

        await context.CommandService.DeleteNodeAsync(UserA, folder.Id, default);
        await context.CommandService.DeleteNodeAsync(UserA, lone, default);

        var trash = await context.QueryService.ListTrashAsync(UserA, default);

        // The folder is a trash root; its child and the separately-deleted file are roots too (their
        // parents are not deleted).
        Assert.Equal(2, trash.Count);
        Assert.DoesNotContain(trash, item => item.Id == child);
        Assert.Contains(trash, item => item.Id == folder.Id);
        Assert.Contains(trash, item => item.Id == lone);
        Assert.All(trash, item => Assert.True(item.PurgeAfterUtc > item.DeletedAtUtc));
    }

    [Fact]
    public async Task Delete_OfAnotherUsersNode_ThrowsNotFound()
    {
        var context = new ApplicationTestContext();
        var folder = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Photos"), default);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            context.CommandService.DeleteNodeAsync(UserB, folder.Id, default));
    }

    private static CompleteUploadRequest CompleteParts(int partCount) =>
        new(Enumerable.Range(1, partCount).Select(number => new UploadedPartRequest(number, $"etag-{number}")).ToList());

    private static async Task<string> InitiateAndCompleteAsync(
        ApplicationTestContext context,
        string userId,
        string parentId,
        string fileName)
    {
        var upload = await context.UploadService.InitiateAsync(
            userId,
            new InitiateUploadRequest(parentId, fileName, "text/plain", 12 * 1024 * 1024),
            default);
        context.Storage.ObjectSizeBytes = 12 * 1024 * 1024;
        var details = await context.UploadService.CompleteAsync(userId, upload.UploadId, CompleteParts(upload.ExpectedPartCount), default);
        return details.Id;
    }
}
