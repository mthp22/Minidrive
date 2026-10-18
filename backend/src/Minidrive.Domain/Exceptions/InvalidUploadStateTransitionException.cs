using Minidrive.Domain.Enums;

namespace Minidrive.Domain.Exceptions;

/// <summary>
/// Thrown when an upload session is asked to move between states that are not connected
/// by a valid transition (for example completing a session that was already aborted).
/// </summary>
public sealed class InvalidUploadStateTransitionException : DomainException
{
    public const string ErrorCode = "upload.transition.invalid";

    public InvalidUploadStateTransitionException(UploadStatus from, UploadStatus to)
        : base(ErrorCode, $"Cannot transition an upload session from '{from}' to '{to}'.")
    {
        From = from;
        To = to;
    }

    public UploadStatus From { get; }

    public UploadStatus To { get; }
}
