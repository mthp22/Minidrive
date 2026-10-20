using Minidrive.Application.Abstractions;
using Minidrive.Application.Abstractions.Persistence;
using Minidrive.Application.Exceptions;
using Minidrive.Application.Models;
using Minidrive.Domain.Entities;
using Minidrive.Domain.Exceptions;
using Minidrive.Domain.ValueObjects;

namespace Minidrive.Application.Services;

/// <summary>
/// Write-side use cases for drive nodes: create folders, rename, move and delete (soft delete with
/// deferred storage cleanup).
/// </summary>
/// <remarks>
/// The hierarchy is expressed with a materialized path (<see cref="MaterializedPath"/>). Descendant
/// rewrites use a single bulk update rather than a recursive walk, so metadata operations never
/// touch object storage.
/// </remarks>
public sealed class NodeCommandService
{
    private readonly INodeRepository _nodes;
    private readonly IStorageCleanupQueue _cleanupQueue;
    private readonly IIdGenerator _ids;
    private readonly TimeProvider _clock;

    public NodeCommandService(
        INodeRepository nodes,
        IStorageCleanupQueue cleanupQueue,
        IIdGenerator ids,
        TimeProvider clock)
    {
        _nodes = nodes;
        _cleanupQueue = cleanupQueue;
        _ids = ids;
        _clock = clock;
    }

    /// <summary>Creates a folder under the given parent (or at the root).</summary>
    public async Task<NodeDetails> CreateFolderAsync(
        string userId,
        CreateFolderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _clock.GetUtcNow();
        var name = NodeName.Validate(request.Name);
        var parentPath = await ResolveParentPathAsync(userId, request.ParentId, cancellationToken);

        if (await _nodes.SiblingNameExistsAsync(userId, request.ParentId, name, excludeNodeId: null, cancellationToken))
        {
            throw new NameConflictException(name);
        }

        var folder = Node.CreateFolder(_ids.NewId(), userId, request.ParentId, parentPath, name, now);

        // The unique sibling-name index is the ultimate guard against a concurrent create.
        await _nodes.InsertAsync(folder, cancellationToken);

        return NodeModelMapper.ToDetails(folder);
    }

    /// <summary>Renames an existing node.</summary>
    public async Task<NodeDetails> RenameNodeAsync(
        string userId,
        string nodeId,
        RenameNodeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _clock.GetUtcNow();
        var node = await GetActiveNodeAsync(userId, nodeId, cancellationToken);
        var name = NodeName.Validate(request.Name);

        if (string.Equals(node.Name, name, StringComparison.Ordinal))
        {
            return NodeModelMapper.ToDetails(node);
        }

        if (await _nodes.SiblingNameExistsAsync(userId, node.ParentId, name, excludeNodeId: node.Id, cancellationToken))
        {
            throw new NameConflictException(name);
        }

        node.Rename(name, now);
        await _nodes.UpdateAsync(node, cancellationToken);

        return NodeModelMapper.ToDetails(node);
    }

    /// <summary>Moves a node to another folder, rewriting descendant paths when it is a folder.</summary>
    public async Task<NodeDetails> MoveNodeAsync(
        string userId,
        string nodeId,
        MoveNodeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _clock.GetUtcNow();
        var node = await GetActiveNodeAsync(userId, nodeId, cancellationToken);

        if (string.Equals(node.ParentId, request.ParentId, StringComparison.Ordinal))
        {
            return NodeModelMapper.ToDetails(node);
        }

        string? newParentPath = null;
        if (request.ParentId is not null)
        {
            var parent = await _nodes.FindAsync(userId, request.ParentId, includePending: false, includeDeleted: false, cancellationToken);
            if (parent is null || !parent.IsFolder)
            {
                throw new NotFoundException("The destination folder was not found.");
            }

            if (node.IsFolder &&
                (string.Equals(parent.Id, node.Id, StringComparison.Ordinal) ||
                 MaterializedPath.IsWithin(parent.MaterializedPath, node.DescendantPrefix)))
            {
                throw new InvalidMoveException("A folder cannot be moved into itself or one of its descendants.");
            }

            newParentPath = parent.MaterializedPath;
        }

        if (await _nodes.SiblingNameExistsAsync(userId, request.ParentId, node.Name, excludeNodeId: node.Id, cancellationToken))
        {
            throw new NameConflictException(node.Name);
        }

        var oldPrefix = node.DescendantPrefix;
        node.MoveTo(request.ParentId, newParentPath, now);
        var newPrefix = node.DescendantPrefix;

        // Rebase descendants first. If the process stops here the node still points at its original
        // parent, and a retry finds nothing left to rebase (the operation is therefore retry-safe).
        if (node.IsFolder && !string.Equals(oldPrefix, newPrefix, StringComparison.Ordinal))
        {
            await _nodes.RebaseDescendantPathsAsync(userId, oldPrefix, newPrefix, now, cancellationToken);
        }

        await _nodes.UpdateAsync(node, cancellationToken);

        return NodeModelMapper.ToDetails(node);
    }

    /// <summary>
    /// Soft-deletes a node and its subtree. Storage objects are deliberately preserved so the item
    /// stays restorable for the whole retention window; they are removed only when the trash entry
    /// is permanently deleted or the retention purge runs.
    /// </summary>
    public async Task DeleteNodeAsync(string userId, string nodeId, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var node = await GetActiveNodeAsync(userId, nodeId, cancellationToken);

        await _nodes.SoftDeleteSubtreeAsync(userId, node.Id, node.DescendantPrefix, now, cancellationToken);
    }

    /// <summary>
    /// Restores a soft-deleted node and every descendant removed as part of the same deletion.
    /// A node that is already live is returned unchanged so a retried restore is idempotent.
    /// </summary>
    public async Task<NodeDetails> RestoreNodeAsync(string userId, string nodeId, CancellationToken cancellationToken)
    {
        var now = _clock.GetUtcNow();
        var node = await _nodes.FindAsync(userId, nodeId, includePending: false, includeDeleted: true, cancellationToken);
        if (node is null)
        {
            throw new NotFoundException("The requested node was not found.");
        }

        if (!node.IsDeleted)
        {
            return NodeModelMapper.ToDetails(node);
        }

        var deletedAt = node.DeletedAtUtc ?? now;
        await _nodes.RestoreSubtreeAsync(userId, node.Id, node.DescendantPrefix, deletedAt, now, cancellationToken);

        node.Restore(now);
        return NodeModelMapper.ToDetails(node);
    }

    /// <summary>
    /// Permanently deletes a soft-deleted node and its same-deletion descendants. Storage objects are
    /// removed through the durable cleanup queue; metadata is removed immediately.
    /// </summary>
    public async Task PurgeNodeAsync(string userId, string nodeId, CancellationToken cancellationToken)
    {
        var node = await _nodes.FindAsync(userId, nodeId, includePending: false, includeDeleted: true, cancellationToken);
        if (node is null || !node.IsDeleted)
        {
            // A live node is deleted through the normal delete endpoint, so a non-trash id is a 404.
            throw new NotFoundException("The requested node was not found.");
        }

        var deletedAt = node.DeletedAtUtc ?? _clock.GetUtcNow();
        var batch = await _nodes.ListDeletedBatchAsync(userId, node.Id, node.DescendantPrefix, deletedAt, cancellationToken);
        await PurgeBatchAsync(userId, batch, cancellationToken);
    }

    /// <summary>Permanently deletes every soft-deleted node owned by the user. Returns the count removed.</summary>
    public async Task<int> EmptyTrashAsync(string userId, CancellationToken cancellationToken)
    {
        var deleted = await _nodes.ListAllDeletedAsync(userId, cancellationToken);
        await PurgeBatchAsync(userId, deleted, cancellationToken);
        return deleted.Count;
    }

    private async Task PurgeBatchAsync(string userId, IReadOnlyList<Node> batch, CancellationToken cancellationToken)
    {
        foreach (var node in batch)
        {
            if (node.IsFile && !string.IsNullOrEmpty(node.StorageKey))
            {
                await _cleanupQueue.EnqueueAsync(userId, node.StorageKey, cancellationToken);
            }
        }

        await _nodes.DeleteManyAsync(userId, batch.Select(node => node.Id).ToList(), cancellationToken);
    }

    private async Task<Node> GetActiveNodeAsync(string userId, string nodeId, CancellationToken cancellationToken)
    {
        var node = await _nodes.FindAsync(userId, nodeId, includePending: false, includeDeleted: false, cancellationToken);
        if (node is null)
        {
            throw new NotFoundException("The requested node was not found.");
        }

        return node;
    }

    private async Task<string?> ResolveParentPathAsync(string userId, string? parentId, CancellationToken cancellationToken)
    {
        if (parentId is null)
        {
            return null;
        }

        var parent = await _nodes.FindAsync(userId, parentId, includePending: false, includeDeleted: false, cancellationToken);
        if (parent is null || !parent.IsFolder)
        {
            throw new NotFoundException("The destination folder was not found.");
        }

        return parent.MaterializedPath;
    }
}
