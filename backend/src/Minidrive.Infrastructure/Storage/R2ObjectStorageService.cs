using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Minidrive.Application.Abstractions.Storage;
using Minidrive.Application.Exceptions;
using Minidrive.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Minidrive.Infrastructure.Storage;

/// <summary>
/// Cloudflare R2 implementation of <see cref="IObjectStorageService"/> built on the AWS SDK for .NET.
/// </summary>
/// <remarks>
/// The service never logs pre-signed URLs or credentials. Provider failures are translated into
/// <see cref="StorageUnavailableException"/> so the API can return a 502 without leaking SDK details.
/// </remarks>
public sealed class R2ObjectStorageService : IObjectStorageService
{
    private readonly IAmazonS3 _client;
    private readonly CloudflareR2Options _options;
    private readonly ILogger<R2ObjectStorageService> _logger;

    public R2ObjectStorageService(
        IAmazonS3 client,
        IOptions<CloudflareR2Options> options,
        ILogger<R2ObjectStorageService> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> InitiateMultipartUploadAsync(string storageKey, string? contentType, CancellationToken cancellationToken)
    {
        var request = new InitiateMultipartUploadRequest
        {
            BucketName = _options.BucketName,
            Key = storageKey,
        };

        if (!string.IsNullOrWhiteSpace(contentType))
        {
            request.ContentType = contentType;
        }

        try
        {
            var response = await _client.InitiateMultipartUploadAsync(request, cancellationToken);
            return response.UploadId;
        }
        catch (AmazonS3Exception exception)
        {
            throw new StorageUnavailableException("Failed to initiate the multipart upload.", exception);
        }
    }

    public Task<PresignedUrl> CreatePresignedPartUrlAsync(
        string storageKey,
        string providerUploadId,
        int partNumber,
        TimeSpan expiration,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        var expiresAt = DateTime.UtcNow.Add(expiration);
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = storageKey,
            Verb = HttpVerb.PUT,
            Expires = expiresAt,
            UploadId = providerUploadId,
            PartNumber = partNumber,
        };

        // Signing is a local computation; no network round-trip happens here.
        var url = MatchEndpointScheme(_client.GetPreSignedURL(request));
        return Task.FromResult(new PresignedUrl(
            new Uri(url),
            new DateTimeOffset(DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc)),
            new Dictionary<string, string>()));
    }

    /// <summary>
    /// The AWS SDK always emits <c>https</c> from <see cref="IAmazonS3.GetPreSignedURL"/>, even when
    /// the client targets a plain-http endpoint (a local MinIO, for example). This rewrites the scheme
    /// to match the configured endpoint so clients can actually reach the URL.
    /// </summary>
    /// <remarks>
    /// Safe: a SigV4 presigned signature covers the host, path and query — never the scheme — so
    /// rewriting it does not invalidate the signature.
    /// </remarks>
    private string MatchEndpointScheme(string url)
    {
        var endpointIsPlainHttp =
            _options.ResolveEndpoint()?.StartsWith("http://", StringComparison.OrdinalIgnoreCase) == true;

        if (endpointIsPlainHttp && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return "http://" + url["https://".Length..];
        }

        return url;
    }

    public async Task<CompletedUpload> CompleteMultipartUploadAsync(
        string storageKey,
        string providerUploadId,
        IReadOnlyList<UploadedPart> parts,
        CancellationToken cancellationToken)
    {
        var request = new CompleteMultipartUploadRequest
        {
            BucketName = _options.BucketName,
            Key = storageKey,
            UploadId = providerUploadId,
            PartETags = parts.Select(part => new PartETag(part.PartNumber, part.ETag)).ToList(),
        };

        try
        {
            var response = await _client.CompleteMultipartUploadAsync(request, cancellationToken);
            return new CompletedUpload(response.ETag, response.Location);
        }
        catch (AmazonS3Exception exception)
        {
            throw new StorageUnavailableException("Failed to complete the multipart upload.", exception);
        }
    }

    public async Task AbortMultipartUploadAsync(string storageKey, string providerUploadId, CancellationToken cancellationToken)
    {
        var request = new AbortMultipartUploadRequest
        {
            BucketName = _options.BucketName,
            Key = storageKey,
            UploadId = providerUploadId,
        };

        try
        {
            await _client.AbortMultipartUploadAsync(request, cancellationToken);
        }
        catch (AmazonS3Exception exception)
        {
            // Aborting an upload that no longer exists is not a failure worth surfacing.
            if (exception.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.LogDebug("Multipart upload was already absent when aborted (expected on retries).");
                return;
            }

            throw new StorageUnavailableException("Failed to abort the multipart upload.", exception);
        }
    }

    public async Task<StoredObjectMetadata?> GetObjectMetadataAsync(string storageKey, CancellationToken cancellationToken)
    {
        var request = new GetObjectMetadataRequest
        {
            BucketName = _options.BucketName,
            Key = storageKey,
        };

        try
        {
            var response = await _client.GetObjectMetadataAsync(request, cancellationToken);
            return new StoredObjectMetadata(response.ContentLength, response.ETag, response.Headers?.ContentType);
        }
        catch (AmazonS3Exception exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
        catch (AmazonS3Exception exception)
        {
            throw new StorageUnavailableException("Failed to read object metadata.", exception);
        }
    }

    public async Task DeleteObjectAsync(string storageKey, CancellationToken cancellationToken)
    {
        var request = new DeleteObjectRequest
        {
            BucketName = _options.BucketName,
            Key = storageKey,
        };

        try
        {
            await _client.DeleteObjectAsync(request, cancellationToken);
        }
        catch (AmazonS3Exception exception)
        {
            throw new StorageUnavailableException("Failed to delete the object.", exception);
        }
    }

    public Task<PresignedUrl> CreatePresignedDownloadUrlAsync(
        string storageKey,
        string? downloadFileName,
        string? contentType,
        TimeSpan expiration,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        var expiresAt = DateTime.UtcNow.Add(expiration);
        var request = new GetPreSignedUrlRequest
        {
            BucketName = _options.BucketName,
            Key = storageKey,
            Verb = HttpVerb.GET,
            Expires = expiresAt,
        };

        if (!string.IsNullOrWhiteSpace(downloadFileName))
        {
            var safeName = SanitizeFileName(downloadFileName);
            request.ResponseHeaderOverrides.ContentDisposition =
                $"attachment; filename=\"{safeName}\"; filename*=UTF-8''{Uri.EscapeDataString(safeName)}";
        }

        if (!string.IsNullOrWhiteSpace(contentType))
        {
            request.ResponseHeaderOverrides.ContentType = contentType;
        }

        var url = MatchEndpointScheme(_client.GetPreSignedURL(request));
        return Task.FromResult(new PresignedUrl(
            new Uri(url),
            new DateTimeOffset(DateTime.SpecifyKind(expiresAt, DateTimeKind.Utc)),
            new Dictionary<string, string>()));
    }

    private static string SanitizeFileName(string fileName)
    {
        // Guard against response-header injection and path-ish characters.
        var sanitized = fileName
            .Replace("\"", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .Replace("\\", "_", StringComparison.Ordinal);

        return string.IsNullOrWhiteSpace(sanitized) ? "download" : sanitized;
    }
}
