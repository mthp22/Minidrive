namespace Minidrive.Domain.Exceptions;

/// <summary>Thrown when a node name violates the application's naming rules.</summary>
public sealed class InvalidNodeNameException : DomainException
{
    public const string ErrorCode = "node.name.invalid";

    public InvalidNodeNameException(string message)
        : base(ErrorCode, message)
    {
    }
}
