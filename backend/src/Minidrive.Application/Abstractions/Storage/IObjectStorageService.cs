namespace Minidrive.Application.Abstractions.Storage;

/// <summary>
/// Object storage boundary. Application workflows depend on this abstraction rather than on
/// <c>IAmazonS3</c>, so storage implementation details never leak into business logic.
/// </summary>
/// <remarks>
/// Object keys are always generated server-side and never supplied by clients. A key is treated
/// as an opaque identifier; it is never proof of ownership.
/// </remarks>
public interface IObjectStorageService
{
    /// <summary>Starts a multipart upload and returns the provider upload id.</summary>
    Task<string> InitiateMultipartUploadAsync(string storageKey, string? contentType, CancellationToken cancellationToken);

    /// <summary>Creates a pre-signed URL that lets the client upload a single part.</summary>
    Task<PresignedUrl> CreatePresignedPartUrlAsync(
        string storageKey,
        string providerUploadId,
        int partNumber,
        TimeSpan expiration,
        CancellationToken cancellationToken);

    /// <summary>Completes a multipart upload from the uploaded parts.</summary>
    Task<CompletedUpload> CompleteMultipartUploadAsync(
        string storageKey,
        string providerUploadId,
        IReadOnlyList<UploadedPart> parts,
        CancellationToken cancellationToken);

    /// <summary>Aborts a multipart upload, discarding any uploaded parts.</summary>
    Task AbortMultipartUploadAsync(string storageKey, string providerUploadId, CancellationToken cancellationToken);

    /// <summary>Reads object metadata, or returns <see langword="null"/> when the object does not exist.</summary>
    Task<StoredObjectMetadata?> GetObjectMetadataAsync(string storageKey, CancellationToken cancellationToken);

    /// <summary>Deletes an object. Deleting a missing object is not an error.</summary>
    Task DeleteObjectAsync(string storageKey, CancellationToken cancellationToken);

    /// <summary>Creates a pre-signed URL for downloading an object.</summary>
    Task<PresignedUrl> CreatePresignedDownloadUrlAsync(
        string storageKey,
        string? downloadFileName,
        string? contentType,
        TimeSpan expiration,
        CancellationToken cancellationToken);
}
