namespace Minidrive.Application.Options;

/// <summary>
/// Configuration for the trash: how long a deleted item stays restorable before background
/// maintenance permanently purges it, and an upper bound on how many trash entries are listed.
/// </summary>
public sealed class TrashOptions
{
    public const string SectionName = "Trash";

    public const int MinimumRetentionDays = 1;

    public const int MaximumRetentionDays = 365;

    /// <summary>Default retention window: one week, matching common file-storage behaviour.</summary>
    public const int DefaultRetentionDays = 7;

    /// <summary>Upper bound on trash entries returned by a single listing.</summary>
    public const int DefaultMaxItems = 200;

    /// <summary>How long a deleted item stays restorable before it is permanently removed.</summary>
    public int RetentionDays { get; set; } = DefaultRetentionDays;

    /// <summary>Maximum number of trash entries returned by a single listing.</summary>
    public int MaxItems { get; set; } = DefaultMaxItems;

    public TimeSpan Retention => TimeSpan.FromDays(RetentionDays);
}
