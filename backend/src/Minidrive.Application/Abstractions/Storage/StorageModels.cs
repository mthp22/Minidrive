namespace Minidrive.Application.Abstractions.Storage;

/// <summary>A single uploaded multipart part, as reported by the client after uploading it.</summary>
/// <param name="PartNumber">One-based part number.</param>
/// <param name="ETag">Provider ETag returned to the client when the part was uploaded.</param>
public sealed record UploadedPart(int PartNumber, string ETag);

/// <summary>A pre-signed URL the client uses to upload one part directly to object storage.</summary>
/// <param name="Url">The pre-signed URL.</param>
/// <param name="ExpiresAtUtc">Absolute expiry of the URL.</param>
/// <param name="RequiredHeaders">Headers the client must send verbatim for the signature to match.</param>
public sealed record PresignedUrl(
    Uri Url,
    DateTimeOffset ExpiresAtUtc,
    IReadOnlyDictionary<string, string> RequiredHeaders);

/// <summary>Metadata reported by the provider for a stored object.</summary>
/// <param name="SizeBytes">Object size in bytes.</param>
/// <param name="ETag">Provider ETag.</param>
/// <param name="ContentType">Stored content type, when available.</param>
public sealed record StoredObjectMetadata(long SizeBytes, string? ETag, string? ContentType);

/// <summary>Result of completing a multipart upload.</summary>
public sealed record CompletedUpload(string? ETag, string? Location);
