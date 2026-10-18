namespace Minidrive.Application.Exceptions;

/// <summary>
/// Thrown when a declared payload (for example a file size or part count) exceeds a supported or
/// configured limit.
/// </summary>
public sealed class PayloadTooLargeException : AppException
{
    public const string ErrorCode = "payload.toolarge";

    public PayloadTooLargeException(string message)
        : base(ErrorCode, message)
    {
    }
}
