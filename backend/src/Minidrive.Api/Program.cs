using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Minidrive.Api.Authentication;
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

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();

// The bearer handler is configured through DI so it can read the same validated options (and the
// resolved signing key) as the token service.
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
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
