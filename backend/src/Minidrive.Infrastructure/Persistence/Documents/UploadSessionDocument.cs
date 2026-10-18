using Minidrive.Domain.Enums;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Minidrive.Infrastructure.Persistence.Documents;

/// <summary>
/// Persistence form of a multipart upload session. The provider upload id and storage key are the
/// trusted, server-side record of an in-flight upload.
/// </summary>
[BsonIgnoreExtraElements]
internal sealed class UploadSessionDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string FileNodeId { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string? ParentId { get; set; }

    public string StorageKey { get; set; } = string.Empty;

    public string ProviderUploadId { get; set; } = string.Empty;

    public string? ContentType { get; set; }

    public long ExpectedSizeBytes { get; set; }

    public long ChunkSizeBytes { get; set; }

    public int ExpectedPartCount { get; set; }

    [BsonRepresentation(BsonType.String)]
    public UploadStatus Status { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public string? FinalETag { get; set; }

    public string? FailureReason { get; set; }
}
