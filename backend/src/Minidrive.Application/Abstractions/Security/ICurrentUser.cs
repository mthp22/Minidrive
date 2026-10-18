namespace Minidrive.Application.Abstractions.Security;

/// <summary>
/// Identity of the caller for the current request. The user id is always taken from validated
/// authentication claims and never from a request body.
/// </summary>
public interface ICurrentUser
{
    /// <summary>The authenticated user id, or <see langword="null"/> when anonymous.</summary>
    string? UserId { get; }

    bool IsAuthenticated { get; }
}
