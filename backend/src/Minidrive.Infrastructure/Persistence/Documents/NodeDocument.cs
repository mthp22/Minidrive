using Minidrive.Domain.Enums;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Minidrive.Infrastructure.Persistence.Documents;

/// <summary>
/// Persistence form of a drive node. Files and folders share this document in the <c>nodes</c>
/// collection. Internal storage details (the storage key) live here and are never returned by the
/// API directly.
/// </summary>
/// <remarks>
/// <see cref="Id"/> and <see cref="ParentId"/> use the native ObjectId representation. The owner's
/// <see cref="UserId"/> is a plain string because it comes from an identity provider's subject
/// claim, which is not necessarily an ObjectId.
/// </remarks>
[BsonIgnoreExtraElements]
internal sealed class NodeDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.ObjectId)]
    public string? ParentId { get; set; }

    public string Name { get; set; } = string.Empty;

    [BsonRepresentation(BsonType.String)]
    public NodeType Type { get; set; }

    public string MaterializedPath { get; set; } = string.Empty;

    public string? ContentType { get; set; }

    public long SizeBytes { get; set; }

    public string? StorageKey { get; set; }

    public string? ETag { get; set; }

    [BsonRepresentation(BsonType.String)]
    public NodeStatus Status { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? DeletedAtUtc { get; set; }
}
