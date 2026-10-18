using Minidrive.Application.Abstractions.Storage;

namespace Minidrive.UnitTests.TestDoubles;

/// <summary>
/// Deterministic object-storage fake. It records interactions and lets tests inject failures, so
/// upload workflows can be verified without any network access.
/// </summary>
public sealed class FakeObjectStorage : IObjectStorageService
{
    private readonly Dictionary<string, StoredObjectMetadata> _objects = new(StringComparer.Ordinal);
    private int _uploadCounter;

    public List<string> InitiatedUploads { get; } = [];

    public List<string> CompletedUploads { get; } = [];

    public List<string> AbortedUploads { get; } = [];

    public List<string> DeletedObjects { get; } = [];

    /// <summary>Size reported by the provider when an object is completed.</summary>
    public long ObjectSizeBytes { get; set; }

    /// <summary>When set, <c>InitiateMultipartUploadAsync</c> throws this exception.</summary>
    public Exception? InitiateException { get; set; }

    /// <summary>When set, <c>CompleteMultipartUploadAsync</c> throws this exception.</summary>
    public Exception? CompleteException { get; set; }

    /// <summary>When set, <c>DeleteObjectAsync</c> throws this exception.</summary>
    public Exception? DeleteException { get; set; }

    public Task<string> InitiateMultipartUploadAsync(string storageKey, string? contentType, CancellationToken cancellationToken)
    {
        if (InitiateException is not null)
        {
            throw InitiateException;
        }

        InitiatedUploads.Add(storageKey);
        return Task.FromResult($"provider-upload-{Interlocked.Increment(ref _uploadCounter)}");
    }

    public Task<PresignedUrl> CreatePresignedPartUrlAsync(
        string storageKey,
        string providerUploadId,
        int partNumber,
        TimeSpan expiration,
        CancellationToken cancellationToken)
    {
        var url = new Uri($"https://storage.test/{Uri.EscapeDataString(storageKey)}?part={partNumber}&uploadId={providerUploadId}");
        return Task.FromResult(new PresignedUrl(url, DateTimeOffset.UtcNow.Add(expiration), new Dictionary<string, string>()));
    }

    public Task<CompletedUpload> CompleteMultipartUploadAsync(
        string storageKey,
        string providerUploadId,
        IReadOnlyList<UploadedPart> parts,
        CancellationToken cancellationToken)
    {
        if (CompleteException is not null)
        {
            throw CompleteException;
        }

        CompletedUploads.Add(storageKey);
        _objects[storageKey] = new StoredObjectMetadata(ObjectSizeBytes, "\"provider-etag\"", "application/octet-stream");
        return Task.FromResult(new CompletedUpload("\"provider-etag\"", null));
    }

    public Task AbortMultipartUploadAsync(string storageKey, string providerUploadId, CancellationToken cancellationToken)
    {
        AbortedUploads.Add(storageKey);
        return Task.CompletedTask;
    }

    public Task<StoredObjectMetadata?> GetObjectMetadataAsync(string storageKey, CancellationToken cancellationToken) =>
        Task.FromResult(_objects.TryGetValue(storageKey, out var metadata) ? metadata : null);

    public Task DeleteObjectAsync(string storageKey, CancellationToken cancellationToken)
    {
        if (DeleteException is not null)
        {
            throw DeleteException;
        }

        DeletedObjects.Add(storageKey);
        _objects.Remove(storageKey);
        return Task.CompletedTask;
    }

    public Task<PresignedUrl> CreatePresignedDownloadUrlAsync(
        string storageKey,
        string? downloadFileName,
        string? contentType,
        TimeSpan expiration,
        CancellationToken cancellationToken)
    {
        var url = new Uri($"https://storage.test/download/{Uri.EscapeDataString(storageKey)}");
        return Task.FromResult(new PresignedUrl(url, DateTimeOffset.UtcNow.Add(expiration), new Dictionary<string, string>()));
    }
}
