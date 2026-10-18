namespace Minidrive.Application.Exceptions;

/// <summary>Thrown when a request conflicts with the current state of a resource.</summary>
public sealed class ConflictException : AppException
{
    public const string ErrorCode = "request.conflict";

    public ConflictException(string message)
        : base(ErrorCode, message)
    {
    }

    public ConflictException(string code, string message)
        : base(code, message)
    {
    }
}
