using Minidrive.Application.Models;
using Minidrive.Application.Options;
using Minidrive.Application.Validation;
using Microsoft.Extensions.Options;
using Xunit;

namespace Minidrive.UnitTests.Application;

public sealed class ValidatorTests
{
    [Fact]
    public async Task CreateFolder_AcceptsAValidRequest()
    {
        var validator = new CreateFolderRequestValidator();

        var result = await validator.ValidateAsync(new CreateFolderRequest(null, "Photos"));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad/name")]
    [InlineData("..")]
    public async Task CreateFolder_RejectsInvalidNames(string name)
    {
        var validator = new CreateFolderRequestValidator();

        var result = await validator.ValidateAsync(new CreateFolderRequest(null, name));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task CreateFolder_RejectsMalformedParentId()
    {
        var validator = new CreateFolderRequestValidator();

        var result = await validator.ValidateAsync(new CreateFolderRequest("not-an-object-id", "Photos"));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task InitiateUpload_AcceptsAValidRequest()
    {
        var validator = CreateInitiateValidator(maxFileSizeBytes: 1024);

        var result = await validator.ValidateAsync(new InitiateUploadRequest(null, "video.mp4", "video/mp4", 512));

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task InitiateUpload_RejectsSizeAboveLimit()
    {
        var validator = CreateInitiateValidator(maxFileSizeBytes: 1024);

        var result = await validator.ValidateAsync(new InitiateUploadRequest(null, "video.mp4", null, 2048));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task InitiateUpload_RejectsNegativeSize()
    {
        var validator = CreateInitiateValidator(maxFileSizeBytes: 1024);

        var result = await validator.ValidateAsync(new InitiateUploadRequest(null, "video.mp4", null, -5));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task CompleteUpload_RequiresAtLeastOnePart()
    {
        var validator = new CompleteUploadRequestValidator();

        var result = await validator.ValidateAsync(new CompleteUploadRequest([]));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task CompleteUpload_AcceptsWellFormedParts()
    {
        var validator = new CompleteUploadRequestValidator();
        var request = new CompleteUploadRequest([new UploadedPartRequest(1, "etag")]);

        var result = await validator.ValidateAsync(request);

        Assert.True(result.IsValid);
    }

    private static InitiateUploadRequestValidator CreateInitiateValidator(long maxFileSizeBytes)
    {
        var options = Microsoft.Extensions.Options.Options.Create(new UploadOptions
        {
            MaxFileSizeBytes = maxFileSizeBytes,
        });

        return new InitiateUploadRequestValidator(options);
    }
}
