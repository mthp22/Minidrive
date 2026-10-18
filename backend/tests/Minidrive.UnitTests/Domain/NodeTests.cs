using Minidrive.Domain.Entities;
using Minidrive.Domain.Enums;
using Minidrive.Domain.Exceptions;
using Xunit;

namespace Minidrive.UnitTests.Domain;

public sealed class NodeTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CreateFolder_IsReadyAndUsesRootLevelPath()
    {
        var folder = Node.CreateFolder("id1", "user1", parentId: null, parentPath: null, name: "Photos", Now);

        Assert.Equal(NodeType.Folder, folder.Type);
        Assert.Equal(NodeStatus.Ready, folder.Status);
        Assert.Equal(",", folder.MaterializedPath);
        Assert.False(folder.IsDeleted);
        Assert.Null(folder.StorageKey);
    }

    [Fact]
    public void CreateFolder_NestsUnderParent()
    {
        var folder = Node.CreateFolder("id2", "user1", "parent", ",p,", "Docs", Now);

        Assert.Equal(",p,parent,", folder.MaterializedPath);
        Assert.Equal(",p,parent,id2,", folder.DescendantPrefix);
    }

    [Fact]
    public void CreatePendingFile_StartsPendingAndCarriesStorageKey()
    {
        var file = Node.CreatePendingFile("f1", "user1", null, null, "notes.txt", "text/plain", "users/u/files/f1", Now);

        Assert.Equal(NodeType.File, file.Type);
        Assert.Equal(NodeStatus.Pending, file.Status);
        Assert.Equal(0, file.SizeBytes);
        Assert.Equal("users/u/files/f1", file.StorageKey);
    }

    [Fact]
    public void MarkUploadComplete_FinalisesMetadata()
    {
        var file = Node.CreatePendingFile("f1", "user1", null, null, "notes.txt", "text/plain", "k", Now);

        file.MarkUploadComplete(1024, "\"etag\"", Now.AddMinutes(1));

        Assert.Equal(NodeStatus.Ready, file.Status);
        Assert.Equal(1024, file.SizeBytes);
        Assert.Equal("\"etag\"", file.ETag);
    }

    [Fact]
    public void MarkUploadComplete_IsIdempotent()
    {
        var file = Node.CreatePendingFile("f1", "user1", null, null, "notes.txt", null, "k", Now);
        file.MarkUploadComplete(1024, "a", Now);
        var updatedAt = file.UpdatedAtUtc;

        file.MarkUploadComplete(2048, "b", Now.AddMinutes(5));

        Assert.Equal(1024, file.SizeBytes);
        Assert.Equal("a", file.ETag);
        Assert.Equal(updatedAt, file.UpdatedAtUtc);
    }

    [Fact]
    public void MarkUploadComplete_OnFolder_Throws()
    {
        var folder = Node.CreateFolder("id1", "user1", null, null, "Photos", Now);

        Assert.Throws<DomainException>(() => folder.MarkUploadComplete(1, "a", Now));
    }

    [Fact]
    public void Rename_UpdatesNameAndTimestamp()
    {
        var folder = Node.CreateFolder("id1", "user1", null, null, "Photos", Now);

        folder.Rename("Pictures", Now.AddMinutes(2));

        Assert.Equal("Pictures", folder.Name);
        Assert.Equal(Now.AddMinutes(2), folder.UpdatedAtUtc);
    }

    [Fact]
    public void Rename_WithInvalidName_Throws()
    {
        var folder = Node.CreateFolder("id1", "user1", null, null, "Photos", Now);

        Assert.Throws<InvalidNodeNameException>(() => folder.Rename("bad/name", Now));
    }

    [Fact]
    public void MoveTo_RecomputesOwnPath()
    {
        var folder = Node.CreateFolder("id1", "user1", "parent", ",p,", "Photos", Now);

        folder.MoveTo("other", ",o,", Now.AddMinutes(1));

        Assert.Equal("other", folder.ParentId);
        Assert.Equal(",o,other,", folder.MaterializedPath);
    }

    [Fact]
    public void MoveTo_WhenDeleted_Throws()
    {
        var folder = Node.CreateFolder("id1", "user1", null, null, "Photos", Now);
        folder.SoftDelete(Now);

        Assert.Throws<InvalidMoveException>(() => folder.MoveTo("x", ",x,", Now));
    }

    [Fact]
    public void SoftDelete_IsIdempotentAndRestoreReverses()
    {
        var folder = Node.CreateFolder("id1", "user1", null, null, "Photos", Now);

        folder.SoftDelete(Now);
        folder.SoftDelete(Now.AddMinutes(1));

        Assert.True(folder.IsDeleted);
        Assert.Equal(Now, folder.DeletedAtUtc);

        folder.Restore(Now.AddMinutes(2));
        Assert.False(folder.IsDeleted);
        Assert.Null(folder.DeletedAtUtc);
    }

    [Fact]
    public void RebasePath_RewritesDescendantPath()
    {
        var child = Node.CreateFolder("c1", "user1", "p", ",p,", "Child", Now);

        child.RebasePath(",newp,c1,", Now.AddMinutes(1));

        Assert.Equal(",newp,c1,", child.MaterializedPath);
        Assert.Equal(",newp,c1,c1,", child.DescendantPrefix);
    }
}
