using System.Text;

namespace Minidrive.Domain.ValueObjects;

/// <summary>
/// Materialized path of a node's ancestors, encoded with a leading and trailing delimiter,
/// for example <c>,parentId,folderId,</c>.
/// </summary>
/// <remarks>
/// The format is deliberately delimiter-wrapped: every ancestor identifier is preceded and
/// followed by <see cref="Delimiter"/>. Because MongoDB ObjectId values are hexadecimal they
/// never contain the delimiter, so a prefix match can never accidentally match a partial
/// identifier (for example <c>,ab,</c> is not a prefix of <c>,abc,</c>).
/// The path of a node that sits directly under the virtual root is <see cref="RootLevelPath"/>.
/// </remarks>
public static class MaterializedPath
{
    /// <summary>Ancestor separator.</summary>
    public const char Delimiter = ',';

    /// <summary>Path of a node whose parent is the (virtual) root.</summary>
    public const string RootLevelPath = ",";

    /// <summary>
    /// Defensive upper bound for a path. Used when rebasing descendant paths with a
    /// MongoDB aggregation update where an explicit substring length is required.
    /// Fits comfortably within MongoDB's 1024-byte index key guidance.
    /// </summary>
    public const int MaxLength = 1024;

    /// <summary>
    /// Builds the path of a child given its parent's path and identifier.
    /// A <see langword="null"/> parent means the virtual root.
    /// </summary>
    public static string ForChild(string? parentPath, string? parentId)
    {
        if (parentId is null)
        {
            return RootLevelPath;
        }

        if (string.IsNullOrEmpty(parentPath))
        {
            throw new Exceptions.DomainException(
                "node.path.invalid",
                "A non-root parent requires a materialized path.");
        }

        return string.Concat(parentPath, parentId, Delimiter);
    }

    /// <summary>
    /// The prefix shared by every descendant of a node. A node is a descendant of another
    /// when its <c>MaterializedPath</c> starts with this prefix.
    /// </summary>
    public static string DescendantPrefix(string nodePath, string nodeId)
    {
        ArgumentException.ThrowIfNullOrEmpty(nodePath);
        ArgumentException.ThrowIfNullOrEmpty(nodeId);

        return string.Concat(nodePath, nodeId, Delimiter);
    }

    /// <summary>True when <paramref name="path"/> starts with the given descendant prefix.</summary>
    public static bool IsWithin(string path, string descendantPrefix)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(descendantPrefix);

        return path.StartsWith(descendantPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rewrites a descendant path when an ancestor moved, replacing <paramref name="oldPrefix"/>
    /// with <paramref name="newPrefix"/> while preserving the descendant suffix.
    /// </summary>
    public static string Rebase(string path, string oldPrefix, string newPrefix)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(oldPrefix);
        ArgumentNullException.ThrowIfNull(newPrefix);

        if (!path.StartsWith(oldPrefix, StringComparison.Ordinal))
        {
            throw new Exceptions.DomainException(
                "node.path.invalid",
                "Cannot rebase a path that does not start with the supplied prefix.");
        }

        return string.Concat(newPrefix, path.AsSpan(oldPrefix.Length));
    }

    /// <summary>
    /// Escapes a prefix so it can be used as the start of an anchored MongoDB regular
    /// expression filter without the delimiter ever being treated as a metacharacter.
    /// </summary>
    public static string ToAnchoredRegexPattern(string descendantPrefix)
    {
        ArgumentException.ThrowIfNullOrEmpty(descendantPrefix);

        var builder = new StringBuilder(descendantPrefix.Length + 1);
        builder.Append('^');
        foreach (var character in descendantPrefix)
        {
            if (!char.IsLetterOrDigit(character))
            {
                builder.Append('\\');
            }

            builder.Append(character);
        }

        return builder.ToString();
    }
}
