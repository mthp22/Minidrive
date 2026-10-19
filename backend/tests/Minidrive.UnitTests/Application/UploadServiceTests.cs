using Minidrive.Application.Exceptions;
using Minidrive.Application.Models;
using Minidrive.Application.Options;
using Minidrive.Domain.Enums;
using Minidrive.Domain.Exceptions;
using Minidrive.UnitTests.TestDoubles;
using Xunit;

namespace Minidrive.UnitTests.Application;

public sealed class UploadServiceTests
{
    private const string UserA = "user-a";
    private const string UserB = "user-b";
    private const long TenMiB = 10 * 1024 * 1024;
    private const long TwentyFiveMiB = 25 * 1024 * 1024;

    [Fact]
    public async Task Initiate_ReservesPendingFileAndActivatesSession()
    {
        var context = new ApplicationTestContext();

        var response = await context.UploadService.InitiateAsync(
            UserA,
            new InitiateUploadRequest(null, "video.mp4", "video/mp4", TwentyFiveMiB),
            default);

        Assert.Equal(3, response.ExpectedPartCount);
        Assert.Equal(UploadStatus.Active.ToString(), response.Status);
        Assert.Equal(TenMiB, response.ChunkSizeBytes);

        var node = context.Nodes.All.Single();
        Assert.Equal(NodeStatus.Pending, node.Status);
        Assert.Equal(response.FileId, node.Id);

        var session = context.Sessions.All.Single();
        Assert.Equal(UploadStatus.Active, session.Status);
        Assert.Equal("provider-upload-1", session.ProviderUploadId);
        Assert.Single(context.Storage.InitiatedUploads);
    }

    [Fact]
    public async Task Initiate_WhenNameTaken_ThrowsConflictWithoutTouchingStorage()
    {
        var context = new ApplicationTestContext();
        await context.CommandService.CreateFolderAsync(UserA, new CreateFolderRequest(null, "video.mp4"), default);

        await Assert.ThrowsAsync<NameConflictException>(() =>
            context.UploadService.InitiateAsync(UserA, new InitiateUploadRequest(null, "video.mp4", "video/mp4", 1024), default));

        Assert.Empty(context.Storage.InitiatedUploads);
        Assert.Empty(context.Sessions.All);
    }

    [Fact]
    public async Task Initiate_AboveSizeLimit_ThrowsPayloadTooLarge()
    {
        var context = new ApplicationTestContext(new UploadOptions { MaxFileSizeBytes = TenMiB });

        await Assert.ThrowsAsync<PayloadTooLargeException>(() =>
            context.UploadService.InitiateAsync(UserA, new InitiateUploadRequest(null, "big.bin", null, TenMiB + 1), default));
    }

    [Fact]
    public async Task Initiate_WithNegativeSize_ThrowsValidation()
    {
        var context = new ApplicationTestContext();

        await Assert.ThrowsAsync<ValidationFailedException>(() =>
            context.UploadService.InitiateAsync(UserA, new InitiateUploadRequest(null, "bad.bin", null, -1), default));
    }

    [Fact]
    public async Task Initiate_WhenStorageFails_RollsBackPendingNode()
    {
        var context = new ApplicationTestContext();
        context.Storage.InitiateException = new InvalidOperationException("storage down");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.UploadService.InitiateAsync(UserA, new InitiateUploadRequest(null, "file.bin", null, 1024), default));

        Assert.Empty(context.Nodes.All);
        Assert.Equal(UploadStatus.Failed, context.Sessions.All.Single().Status);
    }

    [Fact]
    public async Task Initiate_EnforcesQuotaWhenEnabled()
    {
        var context = new ApplicationTestContext(new UploadOptions
        {
            MaxFileSizeBytes = 100 * 1024 * 1024,
            QuotaBytesPerUser = TenMiB,
        });

        await Assert.ThrowsAsync<QuotaExceededException>(() =>
            context.UploadService.InitiateAsync(UserA, new InitiateUploadRequest(null, "big.bin", null, TwentyFiveMiB), default));
    }

    [Fact]
    public async Task CreatePartUrl_ReturnsSignedUrlForActiveSession()
    {
        var context = new ApplicationTestContext();
        var response = await InitiateAsync(context, UserA, "video.mp4", TwentyFiveMiB);

        var part = await context.UploadService.CreatePartUrlAsync(UserA, response.UploadId, 2, default);

        Assert.Equal(2, part.PartNumber);
        Assert.Contains("part=2", part.Url.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreatePartUrl_ByAnotherUser_ThrowsNotFound()
    {
        var context = new ApplicationTestContext();
        var response = await InitiateAsync(context, UserA, "video.mp4", TwentyFiveMiB);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            context.UploadService.CreatePartUrlAsync(UserB, response.UploadId, 1, default));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task CreatePartUrl_WithOutOfRangePartNumber_ThrowsValidation(int partNumber)
    {
        var context = new ApplicationTestContext();
        var response = await InitiateAsync(context, UserA, "video.mp4", TwentyFiveMiB);

        await Assert.ThrowsAsync<ValidationFailedException>(() =>
            context.UploadService.CreatePartUrlAsync(UserA, response.UploadId, partNumber, default));
    }

    [Fact]
    public async Task CreatePartUrl_WhenSessionNotActive_ThrowsConflict()
    {
        var context = new ApplicationTestContext();
        var response = await InitiateAsync(context, UserA, "video.mp4", TwentyFiveMiB);
        await context.UploadService.AbortAsync(UserA, response.UploadId, null, default);

        await Assert.ThrowsAsync<ConflictException>(() =>
            context.UploadService.CreatePartUrlAsync(UserA, response.UploadId, 1, default));
    }

    [Fact]
    public async Task CreatePartUrl_WhenSessionExpired_ThrowsConflict()
    {
        var context = new ApplicationTestContext();
        var response = await InitiateAsync(context, UserA, "video.mp4", TwentyFiveMiB);
        context.Clock.Advance(TimeSpan.FromDays(2));

        await Assert.ThrowsAsync<ConflictException>(() =>
            context.UploadService.CreatePartUrlAsync(UserA, response.UploadId, 1, default));
    }

    [Fact]
    public async Task Complete_FinalisesTheFileFromProviderMetadata()
    {
        var context = new ApplicationTestContext();
        var response = await InitiateAsync(context, UserA, "video.mp4", TwentyFiveMiB);
        context.Storage.ObjectSizeBytes = TwentyFiveMiB;

        var details = await context.UploadService.CompleteAsync(
            UserA,
            response.UploadId,
            CompleteParts(3),
            default);

        Assert.Equal(NodeStatus.Ready, context.Nodes.All.Single().Status);
        Assert.Equal(NodeType.File, details.Type);
        Assert.Equal("\"provider-etag\"", details.ETag);
    }

    [Fact]
    public async Task Complete_IsIdempotentForRepeatedRequests()
    {
        var context = new ApplicationTestContext();
        var response = await InitiateAsync(context, UserA, "video.mp4", TwentyFiveMiB);
        context.Storage.ObjectSizeBytes = TwentyFiveMiB;
        var parts = CompleteParts(3);

        var first = await context.UploadService.CompleteAsync(UserA, response.UploadId, parts, default);
        var second = await context.UploadService.CompleteAsync(UserA, response.UploadId, parts, default);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(context.Storage.CompletedUploads);
    }

    [Fact]
    public async Task Complete_WithIncorrectPartCount_ThrowsAndLeavesSessionActive()
    {
        var context = new ApplicationTestContext();
        var response = await InitiateAsync(context, UserA, "video.mp4", TwentyFiveMiB);

        await Assert.ThrowsAsync<ValidationFailedException>(() =>
            context.UploadService.CompleteAsync(UserA, response.UploadId, CompleteParts(2), default));

        Assert.Equal(UploadStatus.Active, context.Sessions.All.Single().Status);
        Assert.Empty(context.Storage.CompletedUploads);
    }

    [Fact]
    public async Task Complete_WithDuplicateParts_Throws()
    {
        var context = new ApplicationTestContext();
        var response = await InitiateAsync(context, UserA, "video.mp4", TwentyFiveMiB);
        var parts = new CompleteUploadRequest(
        [
            new UploadedPartRequest(1, "a"),
            new UploadedPartRequest(1, "b"),
        ]);

        await Assert.ThrowsAsync<ValidationFailedException>(() =>
            context.UploadService.CompleteAsync(UserA, response.UploadId, parts, default));
    }

    [Fact]
    public async Task Complete_WhenProviderReportsUnexpectedSize_FailsAndReleasesTheName()
    {
        var context = new ApplicationTestContext();
        var response = await InitiateAsync(context, UserA, "video.mp4", TwentyFiveMiB);
        context.Storage.ObjectSizeBytes = TenMiB; // smaller than declared

        await Assert.ThrowsAsync<ValidationFailedException>(() =>
            context.UploadService.CompleteAsync(UserA, response.UploadId, CompleteParts(3), default));

        Assert.Equal(UploadStatus.Failed, context.Sessions.All.Single().Status);
        Assert.Single(context.CleanupQueue.Items);

        // The failed session must not keep the file name reserved: the documented retry path is a
        // fresh upload with the same name.
        Assert.Empty(context.Nodes.All);
        var retry = await InitiateAsync(context, UserA, "video.mp4", TwentyFiveMiB);
        Assert.NotEqual(response.UploadId, retry.UploadId);
    }

    [Fact]
    public async Task Complete_WhenStorageFails_MarksSessionFailedAndReleasesTheName()
    {
        var context = new ApplicationTestContext();
        var response = await InitiateAsync(context, UserA, "video.mp4", TwentyFiveMiB);
        context.Storage.CompleteException = new InvalidOperationException("storage down");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            context.UploadService.CompleteAsync(UserA, response.UploadId, CompleteParts(3), default));

        Assert.Equal(UploadStatus.Failed, context.Sessions.All.Single().Status);
        Assert.Empty(context.Nodes.All);
    }

    [Fact]
    public async Task Complete_OfAbortedSession_ThrowsConflict()
    {
        var context = new ApplicationTestContext();
        var response = await InitiateAsync(context, UserA, "video.mp4", TwentyFiveMiB);
        await context.UploadService.AbortAsync(UserA, response.UploadId, null, default);

        await Assert.ThrowsAsync<ConflictException>(() =>
            context.UploadService.CompleteAsync(UserA, response.UploadId, CompleteParts(3), default));
    }

    [Fact]
    public async Task Abort_ReleasesNameAndAbortsProviderUpload()
    {
        var context = new ApplicationTestContext();
        var response = await InitiateAsync(context, UserA, "video.mp4", TwentyFiveMiB);

        var result = await context.UploadService.AbortAsync(UserA, response.UploadId, "user cancelled", default);

        Assert.Equal(UploadStatus.Aborted.ToString(), result.Status);
        Assert.Empty(context.Nodes.All);
        Assert.Single(context.Storage.AbortedUploads);

        // The released name can be reused immediately.
        var reused = await InitiateAsync(context, UserA, "video.mp4", TwentyFiveMiB);
        Assert.Equal("video.mp4", reused.FileName);
    }

    [Fact]
    public async Task Abort_OfTerminalSession_IsIdempotent()
    {
        var context = new ApplicationTestContext();
        var response = await InitiateAsync(context, UserA, "video.mp4", TwentyFiveMiB);
        await context.UploadService.AbortAsync(UserA, response.UploadId, null, default);

        var second = await context.UploadService.AbortAsync(UserA, response.UploadId, null, default);

        Assert.Equal(UploadStatus.Aborted.ToString(), second.Status);
        Assert.Single(context.Storage.AbortedUploads);
    }

    [Fact]
    public async Task Complete_ByAnotherUser_ThrowsNotFound()
    {
        var context = new ApplicationTestContext();
        var response = await InitiateAsync(context, UserA, "video.mp4", TwentyFiveMiB);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            context.UploadService.CompleteAsync(UserB, response.UploadId, CompleteParts(3), default));
    }

    [Fact]
    public async Task Initiate_UsesServerGeneratedStorageKey()
    {
        var context = new ApplicationTestContext();
        var response = await InitiateAsync(context, UserA, "video.mp4", TwentyFiveMiB);

        var session = context.Sessions.All.Single();
        var expectedKey = $"users/{Uri.EscapeDataString(UserA)}/files/{response.FileId}";

        Assert.Equal(expectedKey, session.StorageKey);
        Assert.Equal(expectedKey, context.Nodes.All.Single().StorageKey);
    }

    [Fact]
    public async Task Initiate_RejectsPartCountsBeyondProviderLimit()
    {
        // A 1 byte chunk would be invalid configuration, but the guard against the provider's
        // 10,000 part ceiling must also hold for a legitimate chunk size.
        var context = new ApplicationTestContext(new UploadOptions
        {
            ChunkSizeBytes = 5 * 1024 * 1024,
            MaxFileSizeBytes = long.MaxValue,
        });

        await Assert.ThrowsAsync<PayloadTooLargeException>(() =>
            context.UploadService.InitiateAsync(
                UserA,
                new InitiateUploadRequest(null, "huge.bin", null, 5L * 1024 * 1024 * 10_001),
                default));
    }

    private static Task<InitiateUploadResponse> InitiateAsync(
        ApplicationTestContext context,
        string userId,
        string fileName,
        long sizeBytes) =>
        context.UploadService.InitiateAsync(
            userId,
            new InitiateUploadRequest(null, fileName, "video/mp4", sizeBytes),
            default);

    private static CompleteUploadRequest CompleteParts(int partCount) =>
        new(Enumerable.Range(1, partCount)
            .Select(number => new UploadedPartRequest(number, $"etag-{number}"))
            .ToList());
}
