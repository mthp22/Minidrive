using Minidrive.Api.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Minidrive.Api.Endpoints;

/// <summary>Body of the development-only token request.</summary>
public sealed record DevTokenRequest(string? UserId);

/// <summary>
/// Development convenience: mints a bearer token for a given user id so the API can be exercised
/// with curl or integration tests before an identity provider exists.
/// </summary>
/// <remarks>
/// The endpoint is not registered at all unless <see cref="AuthenticationOptions.EnableDevTokenEndpoint"/>
/// is enabled, and startup validation forbids enabling it outside the Development environment.
/// </remarks>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(
        this IEndpointRouteBuilder endpoints,
        AuthenticationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!options.EnableDevTokenEndpoint)
        {
            return endpoints;
        }

        endpoints.MapPost("/api/v1/auth/dev-token", CreateDevTokenAsync)
            .WithName("CreateDevToken")
            .WithTags("Auth")
            .WithSummary("Development only: mints a bearer token for the given user id.")
            .AllowAnonymous()
            .Accepts<DevTokenRequest>("application/json")
            .Produces<IssuedToken>()
            .ProducesValidationProblem();

        return endpoints;
    }

    private static Task<Results<Ok<IssuedToken>, ValidationProblem>> CreateDevTokenAsync(
        DevTokenRequest request,
        IAuthTokenService tokens)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return Task.FromResult<Results<Ok<IssuedToken>, ValidationProblem>>(
                TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["userId"] = ["A user id is required."],
                }));
        }

        if (request.UserId.Length > 128)
        {
            return Task.FromResult<Results<Ok<IssuedToken>, ValidationProblem>>(
                TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["userId"] = ["The user id must not exceed 128 characters."],
                }));
        }

        return Task.FromResult<Results<Ok<IssuedToken>, ValidationProblem>>(
            TypedResults.Ok(tokens.IssueToken(request.UserId)));
    }
}
