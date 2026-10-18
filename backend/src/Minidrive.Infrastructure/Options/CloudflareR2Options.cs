namespace Minidrive.Infrastructure.Options;

/// <summary>
/// Cloudflare R2 (S3-compatible) object storage configuration.
/// </summary>
/// <remarks>
/// Credentials are supplied through configuration/environment variables and are never logged or
/// committed. <see cref="Endpoint"/> may be set to point at a local S3-compatible service; otherwise
/// the endpoint is derived from <see cref="AccountId"/> using Cloudflare's documented format.
/// </remarks>
public sealed class CloudflareR2Options
{
    public const string SectionName = "CloudflareR2";

    /// <summary>Cloudflare account id used to derive the R2 endpoint.</summary>
    public string? AccountId { get; set; }

    /// <summary>Explicit service endpoint, overriding <see cref="AccountId"/> (useful for local development).</summary>
    public string? Endpoint { get; set; }

    public string AccessKeyId { get; set; } = string.Empty;

    public string SecretAccessKey { get; set; } = string.Empty;

    public string BucketName { get; set; } = string.Empty;

    /// <summary>Signing region. Cloudflare R2 uses <c>auto</c>.</summary>
    public string Region { get; set; } = "auto";

    /// <summary>R2 requires path-style addressing.</summary>
    public bool ForcePathStyle { get; set; } = true;

    /// <summary>
    /// Resolves the effective service endpoint, preferring an explicit endpoint.
    /// Returns <see langword="null"/> when neither setting is present.
    /// </summary>
    public string? ResolveEndpoint()
    {
        if (!string.IsNullOrWhiteSpace(Endpoint))
        {
            return Endpoint.TrimEnd('/');
        }

        if (!string.IsNullOrWhiteSpace(AccountId))
        {
            return $"https://{AccountId.Trim()}.r2.cloudflarestorage.com";
        }

        return null;
    }
}
