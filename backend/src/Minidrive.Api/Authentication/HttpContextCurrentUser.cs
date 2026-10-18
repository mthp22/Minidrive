using System.Security.Claims;
using Minidrive.Application.Abstractions.Security;

namespace Minidrive.Api.Authentication;

/// <summary>
/// Reads the caller's identity from the authenticated <see cref="ClaimsPrincipal"/>. The user id is
/// always the validated <c>sub</c> claim — never a value supplied by the client in a body, header
/// or query string.
/// </summary>
public sealed class HttpContextCurrentUser : ICurrentUser
{
    /// <summary>Claim type of the subject. Inbound claim mapping is disabled, so the raw name applies.</summary>
    public const string SubjectClaimType = "sub";

    private readonly IHttpContextAccessor _accessor;

    public HttpContextCurrentUser(IHttpContextAccessor accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        _accessor = accessor;
    }

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public string? UserId => Principal?.FindFirst(SubjectClaimType)?.Value;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;
}
