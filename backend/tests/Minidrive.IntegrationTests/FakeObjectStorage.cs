using System.Collections.Concurrent;
using Minidrive.Application.Abstractions.Storage;

namespace Minidrive.IntegrationTests;

/// <summary>
/// In-memory stand-in for Cloudflare R2. It keeps the default test suite free of Docker and real
/// credentials while still exercising every storage code path the API calls.
/// </summary>
/// <remarks>
/// Object metadata is only reported when a size was explicitly configured for the key. That lets a
/// test drive both completion paths: no metadata (the server falls back to the session's expected
/// size) and a deliberate size mismatch (the server rejects the completion and queues cleanup).
/// </remarks>
public sealed class FakeObjectStorage : IObjectStorageService
{
    private readonly ConcurrentDictionary<string, long> _reportedSizes = new();
    private readonly ConcurrentDictionary<string, StoredObjectMetadata> _objects = new();
    private readonly ConcurrentBag<string> _deletedKeys = new();
    private readonly ConcurrentBag<string> _abortedUploads = new();
    private int _uploadCounter;

    /// <summary>Storage keys whose deletion was requested, in order.</summary>
    public IReadOnlyCollection<string> DeletedKeys => _deletedKeys.ToArray();

    /// <summary>Provider upload ids whose multipart upload was aborted.</summary>
    public IReadOnlyCollection<string> AbortedUploads => _abortedUploads.ToArray();

    /// <summary>
    /// Declares the size the provider will report for the object produced by the next completion,
    /// so a test can either satisfy or violate the expected size.
    /// </summary>
    public void ReportSizeForNextCompletion(long sizeBytes) =>
        _reportedSizes.AddOrUpdate("next", sizeBytes, (_, _) => sizeBytes);

    /// <summary>Marks an object as stored with a known size (used to assert deferred deletion).</summary>
    public void SeedObject(string storageKey, long sizeBytes) =>
        _objects[storageKey] = new StoredObjectMetadata(sizeBytes, $"\"seed-{storageKey}\"", "application/octet-stream");

    /// <summary>True when the object exists in this fake.</summary>
    public bool HasObject(string storageKey) => _objects.ContainsKey(storageKey);

    public Task<string> InitiateMultipartUploadAsync(string storageKey, string? contentType, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var providerUploadId = $"fake-upload-{Interlocked.Increment(ref _uploadCounter)}";
        return Task.FromResult(providerUploadId);
    }

    public Task<PresignedUrl> CreatePresignedPartUrlAsync(
        string storageKey,
        string providerUploadId,
        int partNumber,
        TimeSpan expiration,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        var url = new Uri($"https://fake-storage.test/{storageKey}?partNumber={partNumber}&uploadId={providerUploadId}");
        return Task.FromResult(new PresignedUrl(url, DateTimeOffset.UtcNow.Add(expiration), new Dictionary<string, string>
        {
            ["Content-Type"] = "application/octet-stream",
        }));
    }

    public Task<CompletedUpload> CompleteMultipartUploadAsync(
        string storageKey,
        string providerUploadId,
        IReadOnlyList<UploadedPart> parts,
        CancellationToken cancellationToken)
    {
        _ = providerUploadId;
        _ = parts;
        _ = cancellationToken;

        var etag = $"\"etag-{Interlocked.Increment(ref _uploadCounter)}\"";

        if (_reportedSizes.TryRemove("next", out var reportedSize))
        {
            _objects[storageKey] = new StoredObjectMetadata(reportedSize, etag, "application/octet-stream");
        }
        else
        {
            // Metadata unknown: GetObjectMetadataAsync reports nothing for this key.
            _objects[storageKey] = new StoredObjectMetadata(-1, etag, "application/octet-stream");
        }

        return Task.FromResult(new CompletedUpload(etag, $"https://fake-storage.test/{storageKey}"));
    }

    public Task AbortMultipartUploadAsync(string storageKey, string providerUploadId, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _abortedUploads.Add(providerUploadId);
        _objects.TryRemove(storageKey, out _);
        return Task.CompletedTask;
    }

    public Task<StoredObjectMetadata?> GetObjectMetadataAsync(string storageKey, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        if (!_objects.TryGetValue(storageKey, out var metadata))
        {
            return Task.FromResult<StoredObjectMetadata?>(null);
        }

        // -1 encodes "stored, but the fake does not model a size for it".
        return Task.FromResult<StoredObjectMetadata?>(metadata.SizeBytes < 0 ? null : metadata);
    }

    public Task DeleteObjectAsync(string storageKey, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _deletedKeys.Add(storageKey);
        _objects.TryRemove(storageKey, out _);
        return Task.CompletedTask;
    }

    public Task<PresignedUrl> CreatePresignedDownloadUrlAsync(
        string storageKey,
        string? downloadFileName,
        string? contentType,
        TimeSpan expiration,
        CancellationToken cancellationToken)
    {
        _ = downloadFileName;
        _ = contentType;
        _ = cancellationToken;

        var url = new Uri($"https://fake-storage.test/download/{storageKey}");
        return Task.FromResult(new PresignedUrl(url, DateTimeOffset.UtcNow.Add(expiration), new Dictionary<string, string>()));
    }
}
