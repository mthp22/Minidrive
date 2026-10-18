using Minidrive.Domain.Enums;
using Minidrive.Domain.Exceptions;

// Aliased because the entity exposes a 'MaterializedPath' property that would otherwise
// shadow the value-object type of the same name.
using PathValue = Minidrive.Domain.ValueObjects.MaterializedPath;
using NodeName = Minidrive.Domain.ValueObjects.NodeName;

namespace Minidrive.Domain.Entities;

/// <summary>
/// A file or folder in a user's drive. Files and folders share one representation (and one
/// MongoDB collection) so the hierarchy can be queried uniformly.
/// </summary>
/// <remarks>
/// Identifiers are stored as strings holding the 24-character hexadecimal ObjectId form. The
/// domain layer has no dependency on MongoDB, so the conversion to and from <c>ObjectId</c>
/// happens in the infrastructure layer.
/// </remarks>
public sealed class Node
{
    internal Node(
        string id,
        string userId,
        string? parentId,
        string name,
        NodeType type,
        string materializedPath,
        string? contentType,
        long sizeBytes,
        string? storageKey,
        string? eTag,
        NodeStatus status,
        bool isDeleted,
        DateTimeOffset createdAtUtc,
        DateTimeOffset updatedAtUtc,
        DateTimeOffset? deletedAtUtc)
    {
        Id = id;
        UserId = userId;
        ParentId = parentId;
        Name = name;
        Type = type;
        MaterializedPath = materializedPath;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        StorageKey = storageKey;
        ETag = eTag;
        Status = status;
        IsDeleted = isDeleted;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
        DeletedAtUtc = deletedAtUtc;
    }

    public string Id { get; }

    public string UserId { get; }

    /// <summary>Parent folder identifier, or <see langword="null"/> for a root-level node.</summary>
    public string? ParentId { get; private set; }

    public string Name { get; private set; }

    public NodeType Type { get; }

    /// <summary>Delimiter-wrapped ancestor path; see <see cref="ValueObjects.MaterializedPath"/>.</summary>
    public string MaterializedPath { get; private set; }

    /// <summary>Declared content type for files; <see langword="null"/> for folders.</summary>
    public string? ContentType { get; private set; }

    /// <summary>Final size in bytes. Zero until a file upload completes.</summary>
    public long SizeBytes { get; private set; }

    /// <summary>Opaque, server-generated object key for files; <see langword="null"/> for folders.</summary>
    public string? StorageKey { get; private set; }

    /// <summary>Provider ETag recorded after a successful upload.</summary>
    public string? ETag { get; private set; }

    public NodeStatus Status { get; private set; }

    public bool IsDeleted { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public DateTimeOffset? DeletedAtUtc { get; private set; }

    public bool IsFolder => Type == NodeType.Folder;

    public bool IsFile => Type == NodeType.File;

    public bool IsReady => Status == NodeStatus.Ready;

    /// <summary>Prefix shared by every descendant of this node.</summary>
    public string DescendantPrefix => PathValue.DescendantPrefix(MaterializedPath, Id);

    /// <summary>Creates a folder that is immediately usable.</summary>
    public static Node CreateFolder(
        string id,
        string userId,
        string? parentId,
        string? parentPath,
        string? name,
        DateTimeOffset now)
    {
        return new Node(
            id: RequireId(id),
            userId: RequireUserId(userId),
            parentId: parentId,
            name: NodeName.Validate(name),
            type: NodeType.Folder,
            materializedPath: PathValue.ForChild(parentPath, parentId),
            contentType: null,
            sizeBytes: 0,
            storageKey: null,
            eTag: null,
            status: NodeStatus.Ready,
            isDeleted: false,
            createdAtUtc: now,
            updatedAtUtc: now,
            deletedAtUtc: null);
    }

    /// <summary>
    /// Reserves a file node for an in-flight upload. The node is <see cref="NodeStatus.Pending"/>
    /// until <see cref="MarkUploadComplete"/> is called, so it never appears in listings while
    /// the multipart upload is still open.
    /// </summary>
    public static Node CreatePendingFile(
        string id,
        string userId,
        string? parentId,
        string? parentPath,
        string? name,
        string? contentType,
        string storageKey,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        return new Node(
            id: RequireId(id),
            userId: RequireUserId(userId),
            parentId: parentId,
            name: NodeName.Validate(name),
            type: NodeType.File,
            materializedPath: PathValue.ForChild(parentPath, parentId),
            contentType: string.IsNullOrWhiteSpace(contentType) ? null : contentType.Trim(),
            sizeBytes: 0,
            storageKey: storageKey,
            eTag: null,
            status: NodeStatus.Pending,
            isDeleted: false,
            createdAtUtc: now,
            updatedAtUtc: now,
            deletedAtUtc: null);
    }

    /// <summary>Renames the node. Only the name (and audit timestamp) change.</summary>
    public void Rename(string? newName, DateTimeOffset now)
    {
        Name = NodeName.Validate(newName);
        UpdatedAtUtc = now;
    }

    /// <summary>
    /// Reparents the node and recomputes its own materialized path. Descendant paths are
    /// rebased separately by the persistence layer (a single bulk update).
    /// </summary>
    public void MoveTo(string? newParentId, string? newParentPath, DateTimeOffset now)
    {
        if (IsDeleted)
        {
            throw new InvalidMoveException("A deleted node cannot be moved.");
        }

        ParentId = newParentId;
        MaterializedPath = PathValue.ForChild(newParentPath, newParentId);
        UpdatedAtUtc = now;
    }

    /// <summary>
    /// Finalises a pending file after a successful upload. Repeated calls are idempotent so a
    /// retried completion request cannot corrupt metadata.
    /// </summary>
    public void MarkUploadComplete(long sizeBytes, string? eTag, DateTimeOffset now)
    {
        if (Type != NodeType.File)
        {
            throw new DomainException("node.upload.notfile", "Only file nodes can be marked as uploaded.");
        }

        if (sizeBytes < 0)
        {
            throw new DomainException("node.size.invalid", "A file size cannot be negative.");
        }

        if (Status == NodeStatus.Ready)
        {
            return;
        }

        SizeBytes = sizeBytes;
        ETag = eTag;
        Status = NodeStatus.Ready;
        UpdatedAtUtc = now;
    }

    /// <summary>
    /// Marks the node as deleted without physically removing it, so listings can exclude it
    /// while object storage is cleaned up and while the operation stays recoverable.
    /// </summary>
    public void SoftDelete(DateTimeOffset now)
    {
        if (IsDeleted)
        {
            return;
        }

        IsDeleted = true;
        DeletedAtUtc = now;
        UpdatedAtUtc = now;
    }

    /// <summary>Reverses <see cref="SoftDelete"/>; used by recovery/restoration tooling.</summary>
    public void Restore(DateTimeOffset now)
    {
        if (!IsDeleted)
        {
            return;
        }

        IsDeleted = false;
        DeletedAtUtc = null;
        UpdatedAtUtc = now;
    }

    /// <summary>Applies a rebased path to a descendant during an ancestor move.</summary>
    internal void RebasePath(string newMaterializedPath, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(newMaterializedPath);

        MaterializedPath = newMaterializedPath;
        UpdatedAtUtc = now;
    }

    private static string RequireId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return id;
    }

    private static string RequireUserId(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return userId;
    }
}
