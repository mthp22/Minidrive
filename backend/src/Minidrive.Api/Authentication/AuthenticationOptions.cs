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

    /// <summary>
    /// Auth0 tenant domain (e.g. <c>dev-abc123.us.auth0.com</c>), without a scheme. When set
    /// together with <see cref="Auth0Audience"/>, the API also accepts Auth0-issued access tokens
    /// validated against the tenant's JWKS. Leave both empty to run without Auth0.
    /// </summary>
    public string? Auth0Domain { get; set; }

    /// <summary>
    /// Auth0 API identifier (audience) the API accepts, e.g. <c>https://minidrive-api</c>. Must match
    /// the audience requested by the client when it obtains an access token.
    /// </summary>
    public string? Auth0Audience { get; set; }

    /// <summary>True when both Auth0 settings are present and Auth0 validation is active.</summary>
    public bool Auth0Enabled =>
        !string.IsNullOrWhiteSpace(Auth0Domain) && !string.IsNullOrWhiteSpace(Auth0Audience);

    /// <summary>
    /// Normalised Auth0 authority (issuer base, with a trailing slash) derived from
    /// <see cref="Auth0Domain"/>. Tolerates a domain that already carries an <c>https://</c> scheme.
    /// </summary>
    public string? Auth0Authority
    {
        get
        {
            if (!Auth0Enabled)
            {
                return null;
            }

            var domain = Auth0Domain!.Trim();
            if (domain.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                domain = domain["https://".Length..];
            }
            else if (domain.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                domain = domain["http://".Length..];
            }

            return $"https://{domain.TrimEnd('/')}/";
        }
    }
}
