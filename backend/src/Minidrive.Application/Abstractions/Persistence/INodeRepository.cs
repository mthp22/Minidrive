using Minidrive.Application.Common;
using Minidrive.Domain.Entities;

namespace Minidrive.Application.Abstractions.Persistence;

/// <summary>
/// Persistence boundary for drive nodes. It exposes operations that map onto real document
/// database operations for the hierarchy rather than a generic CRUD wrapper; every method is
/// scoped to a user so ownership can never be bypassed by supplying a different identifier.
/// </summary>
public interface INodeRepository
{
    /// <summary>Inserts a new node. Throws on a duplicate sibling name.</summary>
    Task InsertAsync(Node node, CancellationToken cancellationToken);

    /// <summary>
    /// Loads a single node owned by <paramref name="userId"/>. By default pending and deleted
    /// nodes are excluded.
    /// </summary>
    Task<Node?> FindAsync(
        string userId,
        string nodeId,
        bool includePending,
        bool includeDeleted,
        CancellationToken cancellationToken);

    /// <summary>Returns one page of a folder's direct children ordered by (name, id).</summary>
    Task<IReadOnlyList<Node>> ListChildrenAsync(
        string userId,
        string? parentId,
        PageRequest page,
        CancellationToken cancellationToken);

    /// <summary>True when a sibling (optionally excluding one node) already uses the given name.</summary>
    Task<bool> SiblingNameExistsAsync(
        string userId,
        string? parentId,
        string name,
        string? excludeNodeId,
        CancellationToken cancellationToken);

    /// <summary>Replaces the mutable fields of an existing node owned by the user.</summary>
    Task<bool> UpdateAsync(Node node, CancellationToken cancellationToken);

    /// <summary>
    /// Recomputes the materialized path of every descendant of a moved node in one bulk update,
    /// replacing <paramref name="oldPrefix"/> with <paramref name="newPrefix"/>.
    /// Returns the number of rewritten documents.
    /// </summary>
    Task<long> RebaseDescendantPathsAsync(
        string userId,
        string oldPrefix,
        string newPrefix,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Lists every descendant of a node (identified by its descendant prefix).</summary>
    Task<IReadOnlyList<Node>> ListDescendantsAsync(
        string userId,
        string descendantPrefix,
        CancellationToken cancellationToken);

    /// <summary>Soft-deletes a node and every one of its descendants in one bulk update.</summary>
    Task<long> SoftDeleteSubtreeAsync(
        string userId,
        string selfNodeId,
        string descendantPrefix,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Atomically finalises a pending file node; returns false when it is no longer pending.</summary>
    Task<bool> TryMarkReadyAsync(
        string userId,
        string nodeId,
        long sizeBytes,
        string? eTag,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Sums the size of ready file descendants beneath a node.</summary>
    Task<long> SumDescendantFileSizeAsync(
        string userId,
        string descendantPrefix,
        CancellationToken cancellationToken);

    /// <summary>Sums the size of every ready file owned by the user.</summary>
    Task<long> SumUserFileSizeAsync(string userId, CancellationToken cancellationToken);

    /// <summary>Returns the storage keys of ready file descendants beneath a node.</summary>
    Task<IReadOnlyList<string>> ListDescendantFileStorageKeysAsync(
        string userId,
        string descendantPrefix,
        CancellationToken cancellationToken);

    /// <summary>Permanently removes a single node (used to discard a pending upload reservation).</summary>
    Task<bool> DeleteAsync(string userId, string nodeId, CancellationToken cancellationToken);
}
