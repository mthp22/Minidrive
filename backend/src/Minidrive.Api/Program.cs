using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Minidrive.Api.Authentication;
using AuthenticationOptions = Minidrive.Api.Authentication.AuthenticationOptions;
using Minidrive.Api.Endpoints;
using Minidrive.Api.Errors;
using Minidrive.Api.OpenApi;
using Minidrive.Application;
using Minidrive.Application.Abstractions.Security;
using Minidrive.Infrastructure;
using Minidrive.Infrastructure.HealthChecks;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Composition roots. Every layer binds and validates its own configuration at startup, so a
// misconfigured deployment fails fast instead of serving incorrect behaviour.
builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

// ---------------------------------------------------------------------------------------------
// Authentication: JWT bearer with a validated issuer, audience, lifetime and signature.
// ---------------------------------------------------------------------------------------------
builder.Services.AddOptions<AuthenticationOptions>()
    .Bind(builder.Configuration.GetSection(AuthenticationOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<AuthenticationOptions>, AuthenticationOptionsValidator>();
builder.Services.AddSingleton<ISigningKeyProvider, SigningKeyProvider>();
builder.Services.AddSingleton<IAuthTokenService, JwtTokenService>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

// Two token authorities share one default scheme. A policy scheme inspects the token's issuer and
// forwards to the right bearer handler: Auth0 (JWKS, RS256) for tenant-issued access tokens, or the
// local symmetric dev-token scheme for everything else. When Auth0 is not configured, every token
// is handled by the dev scheme, preserving the original single-authority behaviour.
const string DevTokenScheme = "DevTokens";
const string Auth0Scheme = "Auth0";
const string BearerScheme = "MinidriveBearer";

string? auth0Authority = null;
{
    var domain = builder.Configuration["Authentication:Auth0Domain"];
    var audience = builder.Configuration["Authentication:Auth0Audience"];
    if (!string.IsNullOrWhiteSpace(domain) && !string.IsNullOrWhiteSpace(audience))
    {
        var normalised = domain.Trim();
        if (normalised.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            normalised = normalised["https://".Length..];
        }
        else if (normalised.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            normalised = normalised["http://".Length..];
        }

        auth0Authority = $"https://{normalised.TrimEnd('/')}/";
    }
}

builder.Services
    .AddAuthentication(BearerScheme)
    .AddJwtBearer(DevTokenScheme)
    .AddPolicyScheme(BearerScheme, "Minidrive bearer", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            if (auth0Authority is null)
            {
                return DevTokenScheme;
            }

            var token = ExtractBearerToken(context.Request);
            return token is not null && IsIssuedByAuth0(token, auth0Authority)
                ? Auth0Scheme
                : DevTokenScheme;
        };
    });

// The dev-token handler is configured through DI so it reads the same validated options (and the
// resolved signing key) as the token service.
builder.Services
    .AddOptions<JwtBearerOptions>(DevTokenScheme)
    .Configure<ISigningKeyProvider, IOptions<AuthenticationOptions>>((bearer, keys, authentication) =>
    {
        var settings = authentication.Value;

        // Keep the claim type as it appears in the token ("sub") instead of the long CLR name.
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = settings.Issuer,
            ValidateAudience = true,
            ValidAudience = settings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(keys.GetKey()),
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });

if (auth0Authority is not null)
{
    // Auth0: the authority points at the tenant's OIDC discovery document; JwtBearer fetches and
    // caches the signing keys (JWKS) automatically, so RS256 access tokens validate without any
    // local secret. The token's "sub" claim (e.g. "auth0|abc123") becomes the per-user id.
    builder.Services
        .AddOptions<JwtBearerOptions>(Auth0Scheme)
        .Configure<IOptions<AuthenticationOptions>>((bearer, authentication) =>
        {
            var settings = authentication.Value;
            bearer.MapInboundClaims = false;
            bearer.Authority = settings.Auth0Authority;
            bearer.Audience = settings.Auth0Audience;
            bearer.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = settings.Auth0Authority,
                ValidateAudience = true,
                ValidAudience = settings.Auth0Audience,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                NameClaimType = "name",
                ClockSkew = TimeSpan.FromMinutes(1),
            };
        });
}

static string? ExtractBearerToken(HttpRequest request)
{
    var header = request.Headers.Authorization.ToString();
    if (string.IsNullOrEmpty(header) ||
        !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    var token = header["Bearer ".Length..].Trim();
    return string.IsNullOrEmpty(token) ? null : token;
}

static bool IsIssuedByAuth0(string token, string authority)
{
    try
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        return jwt.Issuer.StartsWith(authority, StringComparison.OrdinalIgnoreCase);
    }
    catch
    {
        // Not a readable JWT: let the dev scheme decide (and reject it if it is invalid).
        return false;
    }
}

builder.Services.AddAuthorization();

// Enum-valued fields (for example node type) are rendered as readable strings.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));

// ---------------------------------------------------------------------------------------------
// Problem Details, OpenAPI and health checks.
// ---------------------------------------------------------------------------------------------
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi("v1", options => options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());

builder.Services
    .AddHealthChecks()
    .AddCheck<MongoHealthCheck>("mongodb")
    .AddCheck<ObjectStorageHealthCheck>("object_storage");

var app = builder.Build();

// Exception handling is registered first so every failure — including ones raised while mapping an
// endpoint — is answered with Problem Details.
app.UseExceptionHandler();

// Interactive API documentation.
app.MapOpenApi();
app.MapScalarApiReference();

var authentication = app.Services.GetRequiredService<IOptions<AuthenticationOptions>>().Value;

app.MapHealthEndpoints();
app.MapAuthEndpoints(authentication);
app.MapNodeEndpoints();
app.MapUploadEndpoints();

app.Run();

/// <summary>Makes the implicit Program class visible to the integration test project.</summary>
public partial class Program;
