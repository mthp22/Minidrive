using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Minidrive.Api.Authentication;

/// <summary>
/// Startup validation for <see cref="AuthenticationOptions"/>. A misconfigured deployment fails
/// before serving traffic, and the messages name the offending setting without echoing its value.
/// </summary>
public sealed class AuthenticationOptionsValidator : IValidateOptions<AuthenticationOptions>
{
    private readonly IHostEnvironment _environment;

    public AuthenticationOptionsValidator(IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, AuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add("Authentication:Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("Authentication:Audience is required.");
        }

        if (options.TokenLifetimeMinutes is < 1 or > AuthenticationOptions.MaximumTokenLifetimeMinutes)
        {
            failures.Add(
                $"Authentication:TokenLifetimeMinutes must be between 1 and {AuthenticationOptions.MaximumTokenLifetimeMinutes}.");
        }

        if (string.IsNullOrWhiteSpace(options.SigningKey))
        {
            // Development may run without a committed secret; an ephemeral key is generated at
            // startup. Outside Development the key is required unless Auth0 validates tokens —
            // checked below once the Auth0 settings are known.
        }
        else if (options.SigningKey.Length < AuthenticationOptions.MinimumSigningKeyLength)
        {
            failures.Add(
                $"Authentication:SigningKey must be at least {AuthenticationOptions.MinimumSigningKeyLength} characters long.");
        }

        if (options.EnableDevTokenEndpoint && !_environment.IsDevelopment())
        {
            failures.Add(
                "Authentication:EnableDevTokenEndpoint may only be enabled in the Development environment.");
        }

        // Auth0: both settings must be supplied together, or the API would silently accept tokens
        // for an audience it cannot validate.
        var hasAuth0Domain = !string.IsNullOrWhiteSpace(options.Auth0Domain);
        var hasAuth0Audience = !string.IsNullOrWhiteSpace(options.Auth0Audience);
        if (hasAuth0Domain != hasAuth0Audience)
        {
            failures.Add(
                "Authentication:Auth0Domain and Authentication:Auth0Audience must be set together.");
        }

        // A symmetric signing key is only needed for the local dev-token scheme. When Auth0 is the
        // identity provider, its asymmetric JWKS validates tokens, so no local secret is required.
        if (string.IsNullOrWhiteSpace(options.SigningKey) && !options.Auth0Enabled && !_environment.IsDevelopment())
        {
            failures.Add(
                "Authentication:SigningKey is required outside the Development environment unless Auth0 is configured.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
