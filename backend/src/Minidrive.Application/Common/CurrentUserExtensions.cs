using Minidrive.Application.Abstractions.Security;
using Minidrive.Application.Exceptions;

namespace Minidrive.Application.Common;

/// <summary>Helpers for reading the authenticated identity of the current request.</summary>
public static class CurrentUserExtensions
{
    /// <summary>
    /// Returns the authenticated user id or throws when the caller is anonymous. Callers must never
    /// fall back to a client-supplied identifier.
    /// </summary>
    public static string RequireUserId(this ICurrentUser currentUser)
    {
        ArgumentNullException.ThrowIfNull(currentUser);

        var userId = currentUser.UserId;
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new AuthenticationRequiredException();
        }

        return userId;
    }
}
