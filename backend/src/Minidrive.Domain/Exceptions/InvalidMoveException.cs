namespace Minidrive.Domain.Exceptions;

/// <summary>
/// Thrown when a move would break the folder hierarchy (for example moving a folder into
/// itself or one of its own descendants, or moving a node into a non-folder parent).
/// </summary>
public sealed class InvalidMoveException : DomainException
{
    public const string ErrorCode = "node.move.invalid";

    public InvalidMoveException(string message)
        : base(ErrorCode, message)
    {
    }
}
