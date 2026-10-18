using Minidrive.Domain.Entities;
using Minidrive.Infrastructure.Persistence.Documents;

namespace Minidrive.Infrastructure.Persistence.Mapping;

/// <summary>Maps between the domain <see cref="Node"/> and its persistence document.</summary>
internal static class NodeMapper
{
    public static NodeDocument ToDocument(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return new NodeDocument
        {
            Id = node.Id,
            UserId = node.UserId,
            ParentId = node.ParentId,
            Name = node.Name,
            Type = node.Type,
            MaterializedPath = node.MaterializedPath,
            ContentType = node.ContentType,
            SizeBytes = node.SizeBytes,
            StorageKey = node.StorageKey,
            ETag = node.ETag,
            Status = node.Status,
            IsDeleted = node.IsDeleted,
            CreatedAtUtc = BsonDate.ToBson(node.CreatedAtUtc),
            UpdatedAtUtc = BsonDate.ToBson(node.UpdatedAtUtc),
            DeletedAtUtc = BsonDate.ToBson(node.DeletedAtUtc),
        };
    }

    public static Node ToDomain(NodeDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        // The domain constructor is internal and only used to rehydrate persisted state; it does not
        // re-run validation because the stored data was already validated when it was written.
        return new Node(
            id: document.Id,
            userId: document.UserId,
            parentId: document.ParentId,
            name: document.Name,
            type: document.Type,
            materializedPath: document.MaterializedPath,
            contentType: document.ContentType,
            sizeBytes: document.SizeBytes,
            storageKey: document.StorageKey,
            eTag: document.ETag,
            status: document.Status,
            isDeleted: document.IsDeleted,
            createdAtUtc: BsonDate.ToOffset(document.CreatedAtUtc),
            updatedAtUtc: BsonDate.ToOffset(document.UpdatedAtUtc),
            deletedAtUtc: BsonDate.ToOffset(document.DeletedAtUtc));
    }
}
