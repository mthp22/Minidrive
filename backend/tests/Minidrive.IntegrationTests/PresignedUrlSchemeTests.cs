using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Minidrive.Infrastructure.Options;
using Minidrive.Infrastructure.Storage;
using Xunit;

namespace Minidrive.IntegrationTests;

/// <summary>
/// Regression tests for presigned URL scheme handling. The AWS SDK always emits <c>https</c> from
/// <c>GetPreSignedURL</c> — even when the client targets a plain-http endpoint such as local MinIO —
/// so the storage service rewrites the scheme to match configuration. Without this, part uploads to
/// a local MinIO fail with a TLS error the API can only report as 502 storage.unavailable.
/// </summary>
/// <remarks>
/// Signing is a purely local computation, so these tests need neither MinIO nor R2.
/// </remarks>
public sealed class PresignedUrlSchemeTests
{
    [Fact]
    public async Task Presigned_part_url_follows_a_plain_http_endpoint()
    {
        var service = CreateService("http://127.0.0.1:9000");

        var presigned = await service.CreatePresignedPartUrlAsync(
            "users/u/files/f",
            "provider-upload-id",
            partNumber: 1,
            TimeSpan.FromMinutes(10),
            CancellationToken.None);

        Assert.Equal("http", presigned.Url.Scheme);
        Assert.Equal("127.0.0.1:9000", presigned.Url.Authority);
        Assert.Contains("uploadId=", presigned.Url.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Presigned_download_url_keeps_https_for_secure_endpoints()
    {
        var service = CreateService("https://abc123.r2.cloudflarestorage.com");

        var presigned = await service.CreatePresignedDownloadUrlAsync(
            "users/u/files/f",
            "report.pdf",
            "application/pdf",
            TimeSpan.FromMinutes(10),
            CancellationToken.None);

        // Production R2 endpoints are https and must never be downgraded.
        Assert.Equal("https", presigned.Url.Scheme);
        Assert.Equal("abc123.r2.cloudflarestorage.com", presigned.Url.Authority);
    }

    [Fact]
    public async Task Presigned_urls_never_leak_credentials()
    {
        var service = CreateService("http://127.0.0.1:9000");

        var presigned = await service.CreatePresignedDownloadUrlAsync(
            "users/u/files/f",
            null,
            null,
            TimeSpan.FromMinutes(10),
            CancellationToken.None);

        // Credentials must appear only inside the X-Amz-Credential scope (access key id),
        // never as a query parameter with the secret.
        Assert.DoesNotContain("test-secret-key", presigned.Url.Query, StringComparison.Ordinal);
    }

    private static R2ObjectStorageService CreateService(string endpoint)
    {
        var options = new CloudflareR2Options
        {
            Endpoint = endpoint,
            AccessKeyId = "test-access-key",
            SecretAccessKey = "test-secret-key",
            BucketName = "test-bucket",
            Region = "us-east-1",
        };

        // The same client construction the application uses; no network I/O happens here.
        return new R2ObjectStorageService(
            R2ClientFactory.Create(options),
            Options.Create(options),
            NullLogger<R2ObjectStorageService>.Instance);
    }
}
