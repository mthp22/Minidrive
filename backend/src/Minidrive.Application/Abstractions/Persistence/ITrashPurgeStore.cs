using Minidrive.Domain.Entities;

namespace Minidrive.Application.Abstractions.Persistence;

/// <summary>
/// System-scoped retention operations over soft-deleted nodes. Unlike <see cref="INodeRepository"/>,
/// whose methods are all scoped to a single user so ownership can never be bypassed, these are
/// maintenance operations that intentionally span every user. They are reached only from the
/// background maintenance worker, never from a request handler.
/// </summary>
public interface ITrashPurgeStore
{
    /// <summary>
    /// Returns up to <paramref name="batchSize"/> soft-deleted nodes whose retention window has
    /// elapsed (deleted at or before <paramref name="cutoff"/>), ordered oldest first.
    /// </summary>
    Task<IReadOnlyList<Node>> ListExpiredAsync(
        DateTimeOffset cutoff,
        int batchSize,
        CancellationToken cancellationToken);

    /// <summary>Permanently removes the given nodes. Identifiers are pre-resolved and authorised.</summary>
    Task<long> DeleteManyAsync(IReadOnlyCollection<string> nodeIds, CancellationToken cancellationToken);
}
