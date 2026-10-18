using Minidrive.Domain.Entities;
using Minidrive.Domain.Enums;

namespace Minidrive.Application.Abstractions.Persistence;

/// <summary>
/// Describes one atomic, conditional state change of an upload session. The repository only
/// applies the change when the stored <c>Status</c> still equals <see cref="ExpectedStatus"/>,
/// which makes concurrent and duplicated requests safe without a distributed lock.
/// </summary>
public sealed record UploadSessionTransition(
    string SessionId,
    string UserId,
    UploadStatus ExpectedStatus,
    UploadStatus NewStatus,
    string? ProviderUploadId = null,
    string? FinalETag = null,
    string? FailureReason = null,
    DateTimeOffset? CompletedAtUtc = null);

/// <summary>Persistence boundary for multipart upload sessions.</summary>
public interface IUploadSessionRepository
{
    Task InsertAsync(UploadSession session, CancellationToken cancellationToken);

    /// <summary>Loads a session owned by the user, or <see langword="null"/>.</summary>
    Task<UploadSession?> FindAsync(string userId, string sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically applies <paramref name="transition"/> when the stored status still matches.
    /// Returns the updated session, or <see langword="null"/> when the precondition no longer holds.
    /// </summary>
    Task<UploadSession?> TransitionAsync(
        UploadSessionTransition transition,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns open sessions (Initiating or Active) that expired at or before
    /// <paramref name="now"/>, oldest first, for maintenance cleanup.
    /// </summary>
    Task<IReadOnlyList<UploadSession>> ListExpiredAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken);
}
