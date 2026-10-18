using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Minidrive.Api.Authentication;

/// <summary>Resolves the symmetric signing key used to issue and validate tokens.</summary>
public interface ISigningKeyProvider
{
    /// <summary>Signing key bytes, never logged or exposed through an endpoint.</summary>
    byte[] GetKey();
}

/// <summary>
/// Reads the configured signing key. In the Development environment a missing key is replaced with
/// a random ephemeral one so the API runs without any committed secret; startup validation
/// guarantees a configured key exists in every other environment.
/// </summary>
public sealed class SigningKeyProvider : ISigningKeyProvider
{
    private readonly AuthenticationOptions _options;
    private readonly ILogger<SigningKeyProvider> _logger;
    private byte[]? _ephemeralKey;

    public SigningKeyProvider(IOptions<AuthenticationOptions> options, ILogger<SigningKeyProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;
    }

    public byte[] GetKey()
    {
        if (!string.IsNullOrWhiteSpace(_options.SigningKey))
        {
            return Encoding.UTF8.GetBytes(_options.SigningKey);
        }

        if (_ephemeralKey is null)
        {
            _ephemeralKey = RandomNumberGenerator.GetBytes(64);
            _logger.LogWarning(
                "Authentication:SigningKey is not configured; an ephemeral development key was generated. " +
                "Tokens issued before the last restart will no longer be accepted.");
        }

        return _ephemeralKey;
    }
}
