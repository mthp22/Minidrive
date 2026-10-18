using System.Text.RegularExpressions;

namespace Minidrive.Application.Common;

/// <summary>
/// Validation for identifiers. Nodes and upload sessions use MongoDB ObjectId values, exposed to
/// clients as their 24-character hexadecimal string form.
/// </summary>
public static partial class IdFormat
{
    /// <summary>Maximum identifier length accepted from clients.</summary>
    public const int ObjectIdHexLength = 24;

    [GeneratedRegex("^[0-9a-fA-F]{24}$", RegexOptions.CultureInvariant)]
    private static partial Regex ObjectIdPattern();

    /// <summary>True when the value is a well-formed ObjectId hex string.</summary>
    public static bool IsValidObjectId(string? value) =>
        !string.IsNullOrEmpty(value) && ObjectIdPattern().IsMatch(value);
}
