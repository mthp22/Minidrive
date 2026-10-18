using Minidrive.Application.Exceptions;
using Minidrive.Application.Models;
using Minidrive.Domain.Enums;
using Minidrive.UnitTests.TestDoubles;
using Xunit;

namespace Minidrive.UnitTests.Application;

public sealed class NodeQueryServiceTests
{
    private const string UserA = "user-a";
    private const string UserB = "user-b";

    [Fact]
    public async Task ListChildren_OrdersByNameAndExcludesPendingNodes()
    {
        var context = new ApplicationTestContext();
        await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Bravo"), default);
        await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Alpha"), default);

        // A pending upload must stay invisible until it completes.
        await context.UploadService.InitiateAsync(UserA, new InitiateUploadRequest(null, "Charlie.txt", "text/plain", 12), default);

        var page = await context.QueryService.ListChildrenAsync(UserA, null, pageSize: 50, cursorToken: null, default);

        Assert.Equal(["Alpha", "Bravo"], page.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task ListChildren_OnlyReturnsTheRequestingUsersNodes()
    {
        var context = new ApplicationTestContext();
        await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Mine"), default);
        await context.CommandService.CreateFolderAsync(UserB, new CreateFolderRequest(null, "Theirs"), default);

        var page = await context.QueryService.ListChildrenAsync(UserA, null, pageSize: 50, cursorToken: null, default);

        Assert.Equal(["Mine"], page.Items.Select(item => item.Name));
    }

    [Fact]
    public async Task ListChildren_PaginatesWithOpaqueCursor()
    {
        var context = new ApplicationTestContext();
        await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "A"), default);
        await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "B"), default);
        await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "C"), default);

        var firstPage = await context.QueryService.ListChildrenAsync(UserA, null, pageSize: 2, cursorToken: null, default);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.True(firstPage.HasMore);

        var secondPage = await context.QueryService.ListChildrenAsync(UserA, null, pageSize: 2, cursorToken: firstPage.NextCursor, default);
        Assert.Single(secondPage.Items);
        Assert.False(secondPage.HasMore);

        var all = firstPage.Items.Concat(secondPage.Items).Select(item => item.Name).ToArray();
        Assert.Equal(["A", "B", "C"], all);
    }

    [Fact]
    public async Task ListChildren_WithMalformedCursor_ReturnsFirstPage()
    {
        var context = new ApplicationTestContext();
        await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "A"), default);

        var page = await context.QueryService.ListChildrenAsync(UserA, null, pageSize: 10, cursorToken: "not-a-real-cursor", default);

        Assert.Single(page.Items);
    }

    [Fact]
    public async Task ListChildren_MissingFolder_ThrowsNotFound()
    {
        var context = new ApplicationTestContext();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            context.QueryService.ListChildrenAsync(UserA, "0000000000000000000000ff", pageSize: 10, cursorToken: null, default));
    }

    [Fact]
    public async Task ListChildren_OfAnotherUsersFolder_ThrowsNotFound()
    {
        var context = new ApplicationTestContext();
        var folder = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Private"), default);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            context.QueryService.ListChildrenAsync(UserB, folder.Id, pageSize: 10, cursorToken: null, default));
    }

    [Fact]
    public async Task GetNode_Missing_ThrowsNotFound()
    {
        var context = new ApplicationTestContext();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            context.QueryService.GetNodeAsync(UserA, "0000000000000000000000ff", includeFolderSize: false, default));
    }

    [Fact]
    public async Task GetNode_IncludeFolderSize_SumsRecursively()
    {
        var context = new ApplicationTestContext();
        var root = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Docs"), default);
        var nested = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(root.Id, "Nested"), default);

        await CompleteUploadAsync(context, UserA, root.Id, "top.bin", 12 * 1024 * 1024);
        await CompleteUploadAsync(context, UserA, nested.Id, "deep.bin", 12 * 1024 * 1024);

        var details = await context.QueryService.GetNodeAsync(UserA, root.Id, includeFolderSize: true, default);

        Assert.Equal(24 * 1024 * 1024, details.FolderSizeBytes);
    }

    [Fact]
    public async Task GetNode_WithoutFolderSize_LeavesSizeNull()
    {
        var context = new ApplicationTestContext();
        var root = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Docs"), default);

        var details = await context.QueryService.GetNodeAsync(UserA, root.Id, includeFolderSize: false, default);

        Assert.Null(details.FolderSizeBytes);
    }

    [Fact]
    public async Task CreateDownloadUrl_ForPendingFile_ThrowsNotFound()
    {
        var context = new ApplicationTestContext();
        var upload = await context.UploadService.InitiateAsync(
            UserA,
            new InitiateUploadRequest(null, "pending.bin", "application/octet-stream", 12),
            default);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            context.QueryService.CreateDownloadUrlAsync(UserA, upload.FileId, default));
    }

    [Fact]
    public async Task CreateDownloadUrl_ForFolder_ThrowsNotFound()
    {
        var context = new ApplicationTestContext();
        var folder = await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "Docs"), default);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            context.QueryService.CreateDownloadUrlAsync(UserA, folder.Id, default));
    }

    [Fact]
    public async Task CreateDownloadUrl_ForReadyFile_ReturnsUrl()
    {
        var context = new ApplicationTestContext();
        var fileId = await CompleteUploadAsync(context, UserA, null, "report.bin", 12 * 1024 * 1024);

        var response = await context.QueryService.CreateDownloadUrlAsync(UserA, fileId, default);

        Assert.Equal(fileId, response.FileId);
        Assert.Contains("/download/", response.Url.ToString(), StringComparison.Ordinal);
    }

    private static async Task<string> CompleteUploadAsync(
        ApplicationTestContext context,
        string userId,
        string? parentId,
        string fileName,
        long sizeBytes)
    {
        var upload = await context.UploadService.InitiateAsync(
            userId,
            new InitiateUploadRequest(parentId, fileName, "application/octet-stream", sizeBytes),
            default);

        context.Storage.ObjectSizeBytes = sizeBytes;
        var parts = new CompleteUploadRequest(
            Enumerable.Range(1, upload.ExpectedPartCount)
                .Select(number => new UploadedPartRequest(number, $"etag-{number}"))
                .ToList());

        var details = await context.UploadService.CompleteAsync(userId, upload.UploadId, parts, default);
        Assert.Equal(NodeType.File, details.Type);
        return details.Id;
    }
}
