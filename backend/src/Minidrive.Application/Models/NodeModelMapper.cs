using Minidrive.Domain.Entities;

namespace Minidrive.Application.Models;

/// <summary>Maps domain nodes onto API-facing models, deliberately omitting storage internals.</summary>
public static class NodeModelMapper
{
    public static NodeSummary ToSummary(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return new NodeSummary(
            node.Id,
            node.ParentId,
            node.Name,
            node.Type,
            node.SizeBytes,
            node.ContentType,
            node.CreatedAtUtc,
            node.UpdatedAtUtc);
    }

    public static NodeDetails ToDetails(Node node, long? folderSizeBytes = null)
    {
        ArgumentNullException.ThrowIfNull(node);

        return new NodeDetails(
            node.Id,
            node.ParentId,
            node.Name,
            node.Type,
            node.SizeBytes,
            node.ContentType,
            node.ETag,
            node.CreatedAtUtc,
            node.UpdatedAtUtc,
            folderSizeBytes);
    }

    public static TrashItem ToTrashItem(Node node, TimeSpan retention)
    {
        ArgumentNullException.ThrowIfNull(node);

        var deletedAt = node.DeletedAtUtc ?? node.UpdatedAtUtc;
        return new TrashItem(
            node.Id,
            node.ParentId,
            node.Name,
            node.Type,
            node.SizeBytes,
            node.ContentType,
            deletedAt,
            deletedAt + retention);
    }
}
