using Minidrive.Domain.Exceptions;

namespace Minidrive.Domain.ValueObjects;

/// <summary>Naming rules for files and folders, applied consistently on create and rename.</summary>
public static class NodeName
{
    /// <summary>Maximum length of a node name, in characters.</summary>
    public const int MaxLength = 255;

    /// <summary>Characters that are never allowed inside a node name.</summary>
    private static readonly char[] ForbiddenCharacters = ['/', '\\'];

    /// <summary>
    /// Validates and normalises a node name. Surrounding whitespace is trimmed; every other
    /// rule is enforced verbatim so a rejected name never silently changes shape.
    /// </summary>
    /// <exception cref="InvalidNodeNameException">The name violates a naming rule.</exception>
    public static string Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidNodeNameException("A name is required.");
        }

        var trimmed = name.Trim();

        if (trimmed.Length > MaxLength)
        {
            throw new InvalidNodeNameException($"A name must be at most {MaxLength} characters long.");
        }

        if (trimmed is "." or "..")
        {
            throw new InvalidNodeNameException("'.' and '..' are not valid names.");
        }

        if (trimmed.IndexOfAny(ForbiddenCharacters) >= 0)
        {
            throw new InvalidNodeNameException("A name cannot contain '/' or '\\'.");
        }

        foreach (var character in trimmed)
        {
            if (char.IsControl(character))
            {
                throw new InvalidNodeNameException("A name cannot contain control characters.");
            }
        }

        return trimmed;
    }
}
