namespace Minidrive.Application.Common;

/// <summary>
/// Builds opaque object-storage keys. Keys are always derived from trusted server-side values and
/// are never taken from client input, so a client cannot influence where an object is stored.
/// </summary>
public static class ObjectKeys
{
    private const string UserSegment = "users";
    private const string FileSegment = "files";

    /// <summary>
    /// Builds the key for a user's file, e.g. <c>users/123/files/665f...</c>. The user id is
    /// URL-escaped so a subject claim containing separators cannot escape its namespace.
    /// </summary>
    public static string ForUserFile(string userId, string fileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileId);

        return $"{UserSegment}/{Uri.EscapeDataString(userId)}/{FileSegment}/{fileId}";
    }
}
