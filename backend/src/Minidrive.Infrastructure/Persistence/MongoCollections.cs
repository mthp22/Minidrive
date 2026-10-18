namespace Minidrive.Infrastructure.Persistence;

/// <summary>Collection names used by the drive. Kept in one place so they are never duplicated as literals.</summary>
internal static class MongoCollections
{
    public const string Nodes = "nodes";

    public const string UploadSessions = "upload_sessions";

    public const string StorageCleanups = "storage_cleanups";
}
