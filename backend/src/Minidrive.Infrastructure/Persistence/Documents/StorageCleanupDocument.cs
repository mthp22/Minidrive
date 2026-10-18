using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Minidrive.Infrastructure.Persistence.Documents;

/// <summary>
/// Durable request to delete an object from storage. Rows are written when a node is deleted so a
/// storage outage cannot leak objects; maintenance replays them until they succeed.
/// </summary>
[BsonIgnoreExtraElements]
internal sealed class StorageCleanupDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    public string UserId { get; set; } = string.Empty;

    public string StorageKey { get; set; } = string.Empty;

    public int Attempts { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Set while an item is claimed for processing; null when it is available.</summary>
    public DateTime? ClaimedAtUtc { get; set; }

    public string? LastError { get; set; }
}
