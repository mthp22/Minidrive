using Minidrive.Application.Abstractions.Persistence;
using Minidrive.Application.Common;
using Minidrive.Domain.Entities;
using Minidrive.Domain.Enums;
using Minidrive.Domain.Exceptions;
using Minidrive.Domain.ValueObjects;

namespace Minidrive.UnitTests.TestDoubles;

/// <summary>
/// In-memory <see cref="INodeRepository"/> that reproduces the query semantics the use cases rely
/// on (user scoping, soft-delete visibility, sibling name uniqueness, materialized-path descendant
/// matching and path rebasing). Entities are stored by reference, so a mutation is visible
/// immediately, matching how the real repository persists on the subsequent update call.
/// </summary>
public sealed class InMemoryNodeRepository : INodeRepository
{
    private readonly Dictionary<string, Node> _nodes = new(StringComparer.Ordinal);

    public IReadOnlyCollection<Node> All => _nodes.Values;

    public Task InsertAsync(Node node, CancellationToken cancellationToken)
    {
        if (SiblingExists(node.UserId, node.ParentId, node.Name, excludeNodeId: null))
        {
            throw new NameConflictException(node.Name);
        }

        _nodes[node.Id] = node;
        return Task.CompletedTask;
    }

    public Task<Node?> FindAsync(
        string userId,
        string nodeId,
        bool includePending,
        bool includeDeleted,
        CancellationToken cancellationToken)
    {
        if (!_nodes.TryGetValue(nodeId, out var node) || node.UserId != userId)
        {
            return Task.FromResult<Node?>(null);
        }

        if (!includeDeleted && node.IsDeleted)
        {
            return Task.FromResult<Node?>(null);
        }

        if (!includePending && node.Status != NodeStatus.Ready)
        {
            return Task.FromResult<Node?>(null);
        }

        return Task.FromResult<Node?>(node);
    }

    public Task<IReadOnlyList<Node>> ListChildrenAsync(
        string userId,
        string? parentId,
        PageRequest page,
        CancellationToken cancellationToken)
    {
        var query = _nodes.Values
            .Where(node => node.UserId == userId
                && string.Equals(node.ParentId, parentId, StringComparison.Ordinal)
                && !node.IsDeleted
                && node.Status == NodeStatus.Ready);

        if (page.After is not null)
        {
            var after = page.After;
            query = query.Where(node =>
                string.CompareOrdinal(node.Name, after.Name) > 0
                || (string.Equals(node.Name, after.Name, StringComparison.Ordinal)
                    && string.CompareOrdinal(node.Id, after.Id) > 0));
        }

        IReadOnlyList<Node> results = query
            .OrderBy(node => node.Name, StringComparer.Ordinal)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .Take(page.PageSize)
            .ToList();

        return Task.FromResult(results);
    }

    public Task<bool> SiblingNameExistsAsync(
        string userId,
        string? parentId,
        string name,
        string? excludeNodeId,
        CancellationToken cancellationToken) =>
        Task.FromResult(SiblingExists(userId, parentId, name, excludeNodeId));

    public Task<bool> UpdateAsync(Node node, CancellationToken cancellationToken)
    {
        if (!_nodes.TryGetValue(node.Id, out var existing) || existing.UserId != node.UserId)
        {
            return Task.FromResult(false);
        }

        _nodes[node.Id] = node;
        return Task.FromResult(true);
    }

    public Task<long> RebaseDescendantPathsAsync(
        string userId,
        string oldPrefix,
        string newPrefix,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        long count = 0;

        foreach (var node in _nodes.Values
            .Where(node => node.UserId == userId
                && node.MaterializedPath.StartsWith(oldPrefix, StringComparison.Ordinal))
            .ToList())
        {
            node.RebasePath(MaterializedPath.Rebase(node.MaterializedPath, oldPrefix, newPrefix), now);
            count++;
        }

        return Task.FromResult(count);
    }

    public Task<IReadOnlyList<Node>> ListDescendantsAsync(
        string userId,
        string descendantPrefix,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Node> results = _nodes.Values
            .Where(node => node.UserId == userId
                && !node.IsDeleted
                && node.MaterializedPath.StartsWith(descendantPrefix, StringComparison.Ordinal))
            .ToList();

        return Task.FromResult(results);
    }

    public Task<long> SoftDeleteSubtreeAsync(
        string userId,
        string selfNodeId,
        string descendantPrefix,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        long count = 0;

        foreach (var node in _nodes.Values
            .Where(node => node.UserId == userId
                && !node.IsDeleted
                && (string.Equals(node.Id, selfNodeId, StringComparison.Ordinal)
                    || node.MaterializedPath.StartsWith(descendantPrefix, StringComparison.Ordinal)))
            .ToList())
        {
            node.SoftDelete(now);
            count++;
        }

        return Task.FromResult(count);
    }

    public Task<bool> TryMarkReadyAsync(
        string userId,
        string nodeId,
        long sizeBytes,
        string? eTag,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!_nodes.TryGetValue(nodeId, out var node)
            || node.UserId != userId
            || node.Status != NodeStatus.Pending)
        {
            return Task.FromResult(false);
        }

        node.MarkUploadComplete(sizeBytes, eTag, now);
        return Task.FromResult(true);
    }

    public Task<long> SumDescendantFileSizeAsync(
        string userId,
        string descendantPrefix,
        CancellationToken cancellationToken)
    {
        var total = _nodes.Values
            .Where(node => node.UserId == userId
                && !node.IsDeleted
                && node.IsFile
                && node.Status == NodeStatus.Ready
                && node.MaterializedPath.StartsWith(descendantPrefix, StringComparison.Ordinal))
            .Sum(node => node.SizeBytes);

        return Task.FromResult(total);
    }

    public Task<long> SumUserFileSizeAsync(string userId, CancellationToken cancellationToken)
    {
        var total = _nodes.Values
            .Where(node => node.UserId == userId && !node.IsDeleted && node.IsFile && node.Status == NodeStatus.Ready)
            .Sum(node => node.SizeBytes);

        return Task.FromResult(total);
    }

    public Task<IReadOnlyList<string>> ListDescendantFileStorageKeysAsync(
        string userId,
        string descendantPrefix,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> keys = _nodes.Values
            .Where(node => node.UserId == userId
                && !node.IsDeleted
                && node.IsFile
                && node.Status == NodeStatus.Ready
                && !string.IsNullOrEmpty(node.StorageKey)
                && node.MaterializedPath.StartsWith(descendantPrefix, StringComparison.Ordinal))
            .Select(node => node.StorageKey!)
            .ToList();

        return Task.FromResult(keys);
    }

    public Task<bool> DeleteAsync(string userId, string nodeId, CancellationToken cancellationToken)
    {
        if (!_nodes.TryGetValue(nodeId, out var node) || node.UserId != userId)
        {
            return Task.FromResult(false);
        }

        _nodes.Remove(nodeId);
        return Task.FromResult(true);
    }

    private bool SiblingExists(string userId, string? parentId, string name, string? excludeNodeId) =>
        _nodes.Values.Any(node =>
            node.UserId == userId
            && !node.IsDeleted
            && string.Equals(node.ParentId, parentId, StringComparison.Ordinal)
            && string.Equals(node.Name, name, StringComparison.Ordinal)
            && !string.Equals(node.Id, excludeNodeId, StringComparison.Ordinal));
}
