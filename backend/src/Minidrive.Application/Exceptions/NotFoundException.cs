namespace Minidrive.Application.Exceptions;

/// <summary>
/// Thrown when a resource does not exist or is not owned by the caller. A single 404 is used for
/// both cases so the API never discloses the existence of another user's data.
/// </summary>
public sealed class NotFoundException : AppException
{
    public const string ErrorCode = "resource.notfound";

    public NotFoundException(string message)
        : base(ErrorCode, message)
    {
    }
}
