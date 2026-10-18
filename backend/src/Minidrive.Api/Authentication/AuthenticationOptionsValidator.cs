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
            // startup. Everywhere else an absent key means every token would be forgeable.
            if (!_environment.IsDevelopment())
            {
                failures.Add("Authentication:SigningKey is required outside the Development environment.");
            }
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

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
