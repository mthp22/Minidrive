namespace Minidrive.Domain.Exceptions;

/// <summary>
/// Thrown when a sibling node with the same name already exists in the target folder.
/// </summary>
public sealed class NameConflictException : DomainException
{
    public const string ErrorCode = "node.name.conflict";

    public NameConflictException(string name)
        : base(ErrorCode, $"A node named '{name}' already exists in the target folder.")
    {
        Name = name;
    }

    public string Name { get; }
}
