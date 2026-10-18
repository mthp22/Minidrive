namespace Minidrive.Application.Models;

/// <summary>Initiates a multipart upload for a new file.</summary>
/// <param name="ParentId">Destination folder, or null for the drive root.</param>
/// <param name="FileName">Intended file name.</param>
/// <param name="ContentType">Declared content type; defaults to <c>application/octet-stream</c>.</param>
/// <param name="SizeBytes">Expected total size in bytes.</param>
public sealed record InitiateUploadRequest(
    string? ParentId,
    string FileName,
    string? ContentType,
    long SizeBytes);

/// <summary>
/// Everything the client needs to upload parts. The provider upload id and storage key are
/// intentionally absent: the client only ever references the server-side session id.
/// </summary>
public sealed record InitiateUploadResponse(
    string UploadId,
    string FileId,
    string FileName,
    long ChunkSizeBytes,
    int PartNumberMinimum,
    int PartNumberMaximum,
    int ExpectedPartCount,
    DateTimeOffset ExpiresAtUtc,
    string Status);

/// <summary>A pre-signed URL for one part, plus any headers that must accompany the request.</summary>
public sealed record PartUploadUrlResponse(
    int PartNumber,
    Uri Url,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyDictionary<string, string> Headers);

/// <summary>An uploaded part as reported by the client on completion.</summary>
/// <param name="PartNumber">One-based part number.</param>
/// <param name="ETag">ETag returned by storage when the part was uploaded.</param>
public sealed record UploadedPartRequest(int PartNumber, string ETag);

/// <summary>Completes a multipart upload.</summary>
/// <param name="Parts">Uploaded parts. Order is not significant; the server validates them.</param>
public sealed record CompleteUploadRequest(IReadOnlyList<UploadedPartRequest>? Parts);

/// <summary>Aborts a multipart upload. The reason is optional and never exposed to other users.</summary>
/// <param name="Reason">Optional, non-sensitive reason recorded for diagnostics.</param>
public sealed record AbortUploadRequest(string? Reason);

/// <summary>Result of an abort request.</summary>
public sealed record AbortUploadResponse(string UploadId, string Status);

/// <summary>A short-lived pre-signed URL for downloading a file.</summary>
public sealed record DownloadUrlResponse(
    string FileId,
    string FileName,
    Uri Url,
    DateTimeOffset ExpiresAtUtc);
