using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Minidrive.Api.Authentication;

/// <summary>A freshly minted bearer token and its expiry.</summary>
public sealed record IssuedToken(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc);

/// <summary>
/// Issues bearer tokens for this API. Used only by the development token endpoint and by tests; a
/// real deployment issues tokens from its own identity provider.
/// </summary>
public interface IAuthTokenService
{
    /// <summary>Mints an HMAC-SHA256 signed token whose <c>sub</c> claim is <paramref name="subject"/>.</summary>
    IssuedToken IssueToken(string subject);
}

public sealed class JwtTokenService : IAuthTokenService
{
    private readonly AuthenticationOptions _options;
    private readonly ISigningKeyProvider _keyProvider;
    private readonly TimeProvider _clock;

    public JwtTokenService(
        IOptions<AuthenticationOptions> options,
        ISigningKeyProvider keyProvider,
        TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(keyProvider);
        ArgumentNullException.ThrowIfNull(clock);

        _options = options.Value;
        _keyProvider = keyProvider;
        _clock = clock;
    }

    public IssuedToken IssueToken(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            throw new ArgumentException("A token subject is required.", nameof(subject));
        }

        var now = _clock.GetUtcNow();
        var expires = now.AddMinutes(_options.TokenLifetimeMinutes);

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(_keyProvider.GetKey()),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, subject),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            ],
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        return new IssuedToken(new JwtSecurityTokenHandler().WriteToken(token), "Bearer", expires);
    }
}
