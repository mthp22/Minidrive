namespace Minidrive.Application.Options;

/// <summary>
/// Strongly typed upload configuration. Defaults are safe for Cloudflare R2 and satisfy the
/// provider's multipart constraints (5 MiB minimum part size, 10,000 part ceiling, 5 TiB object ceiling).
/// </summary>
public sealed class UploadOptions
{
    public const string SectionName = "Uploads";

    /// <summary>Default part size: comfortably above the 5 MiB provider minimum.</summary>
    public const long DefaultChunkSizeBytes = 10L * 1024 * 1024;

    /// <summary>Provider minimum size for every part except the last.</summary>
    public const long MinimumPartSizeBytes = 5L * 1024 * 1024;

    /// <summary>Provider maximum number of parts in a single multipart upload.</summary>
    public const int ProviderMaxPartCount = 10_000;

    /// <summary>Provider maximum size of a single object (5 TiB).</summary>
    public const long ProviderMaxObjectSizeBytes = 5L * 1024 * 1024 * 1024 * 1024;

    /// <summary>Part size requested from the client. Must be at least <see cref="MinimumPartSizeBytes"/>.</summary>
    public long ChunkSizeBytes { get; set; } = DefaultChunkSizeBytes;

    /// <summary>Maximum accepted size for a single file.</summary>
    public long MaxFileSizeBytes { get; set; } = 5L * 1024 * 1024 * 1024;

    /// <summary>Lifetime of a pre-signed part or download URL, in minutes.</summary>
    public int PresignedUrlExpirationMinutes { get; set; } = 60;

    /// <summary>How long an upload session may stay open before maintenance expires it.</summary>
    public int UploadSessionLifetimeHours { get; set; } = 24;

    /// <summary>Per-user storage quota in bytes; <c>0</c> disables quota enforcement.</summary>
    public long QuotaBytesPerUser { get; set; }

    public TimeSpan PresignedUrlExpiration => TimeSpan.FromMinutes(PresignedUrlExpirationMinutes);

    public TimeSpan UploadSessionLifetime => TimeSpan.FromHours(UploadSessionLifetimeHours);
}
