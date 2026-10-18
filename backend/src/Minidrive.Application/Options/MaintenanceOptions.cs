namespace Minidrive.Application.Options;

/// <summary>
/// Configuration for the background maintenance work that completes deferred cleanup: expiring
/// abandoned upload sessions and deleting the storage objects of soft-deleted nodes.
/// </summary>
public sealed class MaintenanceOptions
{
    public const string SectionName = "Maintenance";

    public const int MinimumIntervalSeconds = 5;

    public const int MaximumBatchSize = 1000;

    /// <summary>Disables the background worker entirely (for example in read-only tooling or tests).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Delay between two maintenance passes.</summary>
    public int IntervalSeconds { get; set; } = 300;

    /// <summary>Upper bound of sessions expired or objects deleted per pass.</summary>
    public int BatchSize { get; set; } = 100;

    public TimeSpan Interval => TimeSpan.FromSeconds(IntervalSeconds);
}
