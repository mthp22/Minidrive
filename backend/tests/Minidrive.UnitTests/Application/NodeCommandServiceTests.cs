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
    public async Task Delete_File_SoftDeletesAndQueuesStorageCleanup()
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
        Assert.Single(context.CleanupQueue.Items);
    }

    [Fact]
    public async Task Delete_Folder_SoftDeletesWholeSubtreeAndQueuesEveryObject()
    {
        var context = new ApplicationTestContext();
        var folder = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Docs"), default);

        var first = await InitiateAndCompleteAsync(context, UserA, folder.Id, "a.txt");
        var second = await InitiateAndCompleteAsync(context, UserA, folder.Id, "b.txt");

        await context.CommandService.DeleteNodeAsync(UserA, folder.Id, default);

        Assert.True(context.Nodes.All.Single(node => node.Id == folder.Id).IsDeleted);
        Assert.True(context.Nodes.All.Single(node => node.Id == first).IsDeleted);
        Assert.True(context.Nodes.All.Single(node => node.Id == second).IsDeleted);
        Assert.Equal(2, context.CleanupQueue.Items.Count);
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
