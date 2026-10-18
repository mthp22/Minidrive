using Minidrive.Application.Abstractions.Persistence;
using Minidrive.Domain.Entities;
using Minidrive.Domain.Enums;

namespace Minidrive.UnitTests.TestDoubles;

/// <summary>
/// In-memory upload-session store with the same conditional-transition semantics as the MongoDB
/// implementation: a transition only applies when the stored status still matches the expected one.
/// </summary>
public sealed class InMemoryUploadSessionRepository : IUploadSessionRepository
{
    private readonly Dictionary<string, UploadSession> _sessions = new(StringComparer.Ordinal);

    public IReadOnlyCollection<UploadSession> All => _sessions.Values;

    public Task InsertAsync(UploadSession session, CancellationToken cancellationToken)
    {
        _sessions[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task<UploadSession?> FindAsync(string userId, string sessionId, CancellationToken cancellationToken)
    {
        if (_sessions.TryGetValue(sessionId, out var session) && session.UserId == userId)
        {
            return Task.FromResult<UploadSession?>(session);
        }

        return Task.FromResult<UploadSession?>(null);
    }

    public Task<UploadSession?> TransitionAsync(
        UploadSessionTransition transition,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (!_sessions.TryGetValue(transition.SessionId, out var session))
        {
            return Task.FromResult<UploadSession?>(null);
        }

        if (session.UserId != transition.UserId || session.Status != transition.ExpectedStatus)
        {
            return Task.FromResult<UploadSession?>(null);
        }

        switch (transition.NewStatus)
        {
            case UploadStatus.Active:
                session.Activate(transition.ProviderUploadId!, now);
                break;
            case UploadStatus.Completing:
                session.BeginCompleting(now);
                break;
            case UploadStatus.Completed:
                session.MarkCompleted(transition.FinalETag, now);
                break;
            case UploadStatus.Aborting:
                session.BeginAbort(now);
                break;
            case UploadStatus.Aborted:
                session.MarkAborted(now);
                break;
            case UploadStatus.Failed:
                session.MarkFailed(transition.FailureReason ?? "failed", now);
                break;
            case UploadStatus.Expired:
                session.MarkExpired(now);
                break;
            default:
                throw new InvalidOperationException($"Unsupported transition target {transition.NewStatus}.");
        }

        return Task.FromResult<UploadSession?>(session);
    }

    public Task<IReadOnlyList<UploadSession>> ListExpiredAsync(
        DateTimeOffset now,
        int limit,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<UploadSession> expired = _sessions.Values
            .Where(session => session.Status is UploadStatus.Initiating or UploadStatus.Active)
            .Where(session => session.ExpiresAtUtc <= now)
            .OrderBy(session => session.ExpiresAtUtc)
            .Take(limit)
            .ToList();

        return Task.FromResult(expired);
    }
}
