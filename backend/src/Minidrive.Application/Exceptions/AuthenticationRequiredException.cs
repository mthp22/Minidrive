namespace Minidrive.Application.Exceptions;

/// <summary>Thrown when an operation requires an authenticated user but none is present.</summary>
public sealed class AuthenticationRequiredException : AppException
{
    public const string ErrorCode = "auth.required";

    public AuthenticationRequiredException()
        : base(ErrorCode, "Authentication is required to access this resource.")
    {
    }
}
