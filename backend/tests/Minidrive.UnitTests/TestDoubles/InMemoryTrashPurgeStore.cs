using Minidrive.Application.Abstractions.Persistence;
using Minidrive.Domain.Entities;

namespace Minidrive.UnitTests.TestDoubles;

/// <summary>
/// In-memory <see cref="ITrashPurgeStore"/> sharing the same node set as
/// <see cref="InMemoryNodeRepository"/>, so retention tests can drive expiry against the live store.
/// </summary>
public sealed class InMemoryTrashPurgeStore : ITrashPurgeStore
{
    private readonly InMemoryNodeRepository _nodes;

    public InMemoryTrashPurgeStore(InMemoryNodeRepository nodes)
    {
        _nodes = nodes;
    }

    public Task<IReadOnlyList<Node>> ListExpiredAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Node> results = _nodes.All
            .Where(node => node.IsDeleted && node.DeletedAtUtc is not null && node.DeletedAtUtc <= cutoff)
            .OrderBy(node => node.DeletedAtUtc)
            .Take(batchSize)
            .ToList();

        return Task.FromResult(results);
    }

    public async Task<long> DeleteManyAsync(IReadOnlyCollection<string> nodeIds, CancellationToken cancellationToken)
    {
        var idSet = nodeIds.ToHashSet(StringComparer.Ordinal);
        long count = 0;

        foreach (var node in _nodes.All.Where(node => idSet.Contains(node.Id)).ToList())
        {
            if (await _nodes.DeleteAsync(node.UserId, node.Id, cancellationToken))
            {
                count++;
            }
        }

        return count;
    }
}
