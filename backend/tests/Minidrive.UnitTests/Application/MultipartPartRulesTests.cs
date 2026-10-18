using Minidrive.Application.Abstractions.Storage;
using Minidrive.Application.Common;
using Minidrive.Application.Exceptions;
using Xunit;

namespace Minidrive.UnitTests.Application;

public sealed class MultipartPartRulesTests
{
    private const long FiveMiB = 5 * 1024 * 1024;

    [Fact]
    public void ValidateAndOrder_SortsPartsAndNormalisesETags()
    {
        var parts = new[]
        {
            new UploadedPart(2, "\"bbbb\""),
            new UploadedPart(1, "\"aaaa\""),
        };

        var ordered = MultipartPartRules.ValidateAndOrder(parts, expectedPartCount: 2);

        Assert.Equal([1, 2], ordered.Select(part => part.PartNumber));
        Assert.Equal(["aaaa", "bbbb"], ordered.Select(part => part.ETag));
    }

    [Fact]
    public void ValidateAndOrder_RejectsMissingParts()
    {
        Assert.Throws<ValidationFailedException>(() => MultipartPartRules.ValidateAndOrder([], expectedPartCount: 1));
        Assert.Throws<ValidationFailedException>(() => MultipartPartRules.ValidateAndOrder(null, expectedPartCount: 1));
    }

    [Fact]
    public void ValidateAndOrder_RejectsDuplicates()
    {
        var parts = new[] { new UploadedPart(1, "aa"), new UploadedPart(1, "bb") };

        Assert.Throws<ValidationFailedException>(() => MultipartPartRules.ValidateAndOrder(parts, expectedPartCount: 1));
    }

    [Fact]
    public void ValidateAndOrder_RejectsGaps()
    {
        var parts = new[] { new UploadedPart(1, "aa"), new UploadedPart(3, "cc") };

        Assert.Throws<ValidationFailedException>(() => MultipartPartRules.ValidateAndOrder(parts, expectedPartCount: 3));
    }

    [Fact]
    public void ValidateAndOrder_RejectsPartCountMismatch()
    {
        var parts = new[] { new UploadedPart(1, "aa") };

        Assert.Throws<ValidationFailedException>(() => MultipartPartRules.ValidateAndOrder(parts, expectedPartCount: 2));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad etag")]
    [InlineData("bad\tetag")]
    public void ValidateAndOrder_RejectsInvalidETags(string eTag)
    {
        var parts = new[] { new UploadedPart(1, eTag) };

        Assert.Throws<ValidationFailedException>(() => MultipartPartRules.ValidateAndOrder(parts, expectedPartCount: 1));
    }

    [Fact]
    public void EnsureMinimumNonFinalPartSizes_AllowsSinglePartBelowMinimum()
    {
        MultipartPartRules.EnsureMinimumNonFinalPartSizes(objectSizeBytes: 10, partCount: 1, FiveMiB);
    }

    [Fact]
    public void EnsureMinimumNonFinalPartSizes_RejectsObjectTooSmallForPartCount()
    {
        Assert.Throws<ValidationFailedException>(() =>
            MultipartPartRules.EnsureMinimumNonFinalPartSizes(objectSizeBytes: FiveMiB, partCount: 3, FiveMiB));
    }

    [Fact]
    public void EnsureMinimumNonFinalPartSizes_AcceptsSufficientSize()
    {
        MultipartPartRules.EnsureMinimumNonFinalPartSizes(objectSizeBytes: 2 * FiveMiB, partCount: 3, FiveMiB);
    }
}
