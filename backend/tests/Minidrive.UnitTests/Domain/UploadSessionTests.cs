using Minidrive.Domain.Entities;
using Minidrive.Domain.Enums;
using Minidrive.Domain.Exceptions;
using Xunit;

namespace Minidrive.UnitTests.Domain;

public sealed class UploadSessionTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    private static UploadSession Create(long sizeBytes = 25 * 1024 * 1024, long chunkSize = 10 * 1024 * 1024) =>
        UploadSession.Create(
            "session1",
            "user1",
            "file1",
            parentId: null,
            "users/u/files/file1",
            "application/octet-stream",
            sizeBytes,
            chunkSize,
            Now,
            Lifetime);

    [Theory]
    [InlineData(0, 10, 1)]
    [InlineData(1, 10, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(11, 10, 2)]
    [InlineData(25, 10, 3)]
    public void Create_ComputesExpectedPartCount(long size, long chunk, int expectedParts)
    {
        var session = Create(size, chunk);

        Assert.Equal(expectedParts, session.ExpectedPartCount);
    }

    [Fact]
    public void Create_SetsExpiryFromLifetime()
    {
        var session = Create();

        Assert.Equal(Now.Add(Lifetime), session.ExpiresAtUtc);
        Assert.Equal(UploadStatus.Initiating, session.Status);
    }

    [Fact]
    public void Activate_MovesToActiveAndStoresProviderId()
    {
        var session = Create();

        session.Activate("provider-id", Now);

        Assert.Equal(UploadStatus.Active, session.Status);
        Assert.Equal("provider-id", session.ProviderUploadId);
    }

    [Fact]
    public void Activate_WhenAlreadyActive_Throws()
    {
        var session = Create();
        session.Activate("p", Now);

        Assert.Throws<InvalidUploadStateTransitionException>(() => session.Activate("p2", Now));
    }

    [Fact]
    public void Complete_FollowsHappyPath()
    {
        var session = Create();
        session.Activate("p", Now);
        session.BeginCompleting(Now);

        session.MarkCompleted("etag", Now);

        Assert.Equal(UploadStatus.Completed, session.Status);
        Assert.Equal("etag", session.FinalETag);
        Assert.Equal(Now, session.CompletedAtUtc);
        Assert.True(session.IsTerminal);
    }

    [Fact]
    public void BeginCompleting_FromInitiating_Throws()
    {
        var session = Create();

        Assert.Throws<InvalidUploadStateTransitionException>(() => session.BeginCompleting(Now));
    }

    [Fact]
    public void MarkCompleted_WithoutCompleting_Throws()
    {
        var session = Create();
        session.Activate("p", Now);

        Assert.Throws<InvalidUploadStateTransitionException>(() => session.MarkCompleted("etag", Now));
    }

    [Fact]
    public void Abort_FromActive_ReachesAborted()
    {
        var session = Create();
        session.Activate("p", Now);

        session.BeginAbort(Now);
        session.MarkAborted(Now);

        Assert.Equal(UploadStatus.Aborted, session.Status);
        Assert.True(session.IsTerminal);
    }

    [Fact]
    public void Expire_FromAborting_IsAllowed()
    {
        var session = Create();
        session.Activate("p", Now);
        session.BeginAbort(Now);

        session.MarkExpired(Now);

        Assert.Equal(UploadStatus.Expired, session.Status);
    }

    [Theory]
    [InlineData(UploadStatus.Completed)]
    [InlineData(UploadStatus.Aborted)]
    [InlineData(UploadStatus.Failed)]
    [InlineData(UploadStatus.Expired)]
    public void TerminalStates_HaveNoTransitions(UploadStatus status)
    {
        Assert.False(UploadSession.IsTransitionAllowed(status, UploadStatus.Active));
        Assert.False(UploadSession.IsTransitionAllowed(status, UploadStatus.Completing));
    }

    [Fact]
    public void HasExpired_UsesLifetime()
    {
        var session = Create();

        Assert.False(session.HasExpired(Now.AddHours(23)));
        Assert.True(session.HasExpired(Now.AddHours(24)));
    }

    [Fact]
    public void HasExpired_IsFalseForTerminalSessions()
    {
        var session = Create();
        session.Activate("p", Now);
        session.BeginCompleting(Now);
        session.MarkCompleted("e", Now);

        Assert.False(session.HasExpired(Now.AddYears(1)));
    }

    [Fact]
    public void Create_WithNegativeSize_Throws()
    {
        Assert.Throws<DomainException>(() => Create(-1));
    }
}
