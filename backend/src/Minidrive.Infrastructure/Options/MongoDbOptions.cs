namespace Minidrive.Infrastructure.Options;

/// <summary>MongoDB persistence configuration.</summary>
public sealed class MongoDbOptions
{
    public const string SectionName = "MongoDB";

    /// <summary>Standard MongoDB connection string. Never logged.</summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>Database that holds the drive collections.</summary>
    public string DatabaseName { get; set; } = "minidrive";

    /// <summary>
    /// When true, indexes are created (idempotently) during application startup. Disable when
    /// indexes are managed by a separate migration step.
    /// </summary>
    public bool EnsureIndexesOnStartup { get; set; } = true;
}
