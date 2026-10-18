using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace Minidrive.Api.OpenApi;

/// <summary>
/// Completes the generated OpenAPI document: it stamps the API's title/version/description and adds
/// the HTTP bearer security scheme so the OpenAPI UI (Scalar) can authenticate against the API.
/// </summary>
/// <remarks>
/// In ASP.NET Core 9 the document metadata is applied by a transformer rather than through
/// <c>OpenApiOptions</c>, which carries no <c>Info</c> member.
/// </remarks>
public sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public const string SchemeName = "bearer";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);

        document.Info = new OpenApiInfo
        {
            Title = "Minidrive API",
            Version = "v1",
            Description = """
                A Dropbox-style file storage API: folders, cursor-paginated listings, rename/move,
                soft delete with deferred storage cleanup, and three-phase multipart uploads backed by
                MongoDB (metadata) and Cloudflare R2 (objects).
                """,
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, OpenApiSecurityScheme>();
        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Paste a JWT issued by your identity provider (or by the development token endpoint).",
        };

        document.SecurityRequirements ??= new List<OpenApiSecurityRequirement>();
        document.SecurityRequirements.Add(
            new OpenApiSecurityRequirement
            {
                [
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Id = SchemeName,
                            Type = ReferenceType.SecurityScheme,
                        },
                    }
                ] = Array.Empty<string>(),
            });

        return Task.CompletedTask;
    }
}
