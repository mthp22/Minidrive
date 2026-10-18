namespace Minidrive.Api.Authentication;

/// <summary>
/// JWT bearer authentication configuration. The signing key is supplied through configuration or
/// environment variables and is never committed or logged.
/// </summary>
public sealed class AuthenticationOptions
{
    public const string SectionName = "Authentication";

    /// <summary>Minimum accepted length for the symmetric signing key, in characters.</summary>
    public const int MinimumSigningKeyLength = 32;

    /// <summary>Maximum accepted token lifetime, in minutes (seven days).</summary>
    public const int MaximumTokenLifetimeMinutes = 10_080;

    /// <summary>Value of the <c>iss</c> claim this API accepts and issues.</summary>
    public string Issuer { get; set; } = "minidrive";

    /// <summary>Value of the <c>aud</c> claim this API accepts and issues.</summary>
    public string Audience { get; set; } = "minidrive-api";

    /// <summary>
    /// Symmetric signing key (HMAC-SHA256). Required outside the Development environment. In
    /// Development an absent key is replaced with a random ephemeral key at startup so that no
    /// secret ever has to be committed to the repository.
    /// </summary>
    public string? SigningKey { get; set; }

    /// <summary>Lifetime of issued tokens, in minutes.</summary>
    public int TokenLifetimeMinutes { get; set; } = 60;

    /// <summary>
    /// Exposes <c>POST /api/v1/auth/dev-token</c>, which mints a bearer token for an arbitrary user
    /// id without an identity provider. Development convenience only: startup validation rejects it
    /// outside the Development environment.
    /// </summary>
    public bool EnableDevTokenEndpoint { get; set; }
}
