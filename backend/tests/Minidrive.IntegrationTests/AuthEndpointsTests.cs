using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Minidrive.IntegrationTests;

/// <summary>Authentication pipeline: token minting, rejection of bad tokens, ownership masking.</summary>
public sealed class AuthEndpointsTests : ApiTestBase
{
    public AuthEndpointsTests(IntegrationFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Dev_token_endpoint_issues_a_token_that_the_api_accepts()
    {
        var userId = NewUserId();
        var client = CreateAnonymousClient();

        var issueResponse = await PostJsonAsync(client, "/api/v1/auth/dev-token", new { userId });
        Assert.Equal(HttpStatusCode.OK, issueResponse.StatusCode);

        var token = await ReadAsync<IssuedTokenDto>(issueResponse);
        Assert.NotNull(token);
        Assert.Equal("Bearer", token.TokenType);
        Assert.True(token.ExpiresAtUtc > DateTimeOffset.UtcNow);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        var nodesResponse = await client.GetAsync("/api/v1/nodes");
        Assert.Equal(HttpStatusCode.OK, nodesResponse.StatusCode);
    }

    [Fact]
    public async Task Dev_token_endpoint_requires_a_user_id()
    {
        var response = await PostJsonAsync(CreateAnonymousClient(), "/api/v1/auth/dev-token", new { userId = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadProblemAsync(response);
        Assert.NotNull(problem.Errors);
        Assert.Contains("userId", problem.Errors.Keys);
    }

    [Fact]
    public async Task Malformed_token_is_rejected()
    {
        var client = CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-jwt");

        var response = await client.GetAsync("/api/v1/nodes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_signed_with_a_different_key_is_rejected()
    {
        var foreignCredentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes("a-completely-different-signing-key-0123456789")),
            SecurityAlgorithms.HmacSha256);

        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: "minidrive",
            audience: "minidrive-api",
            claims: [new Claim("sub", NewUserId())],
            notBefore: now,
            expires: now.AddMinutes(5),
            signingCredentials: foreignCredentials);

        var client = CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));

        var response = await client.GetAsync("/api/v1/nodes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes("integration-test-signing-key-0123456789abcdef")),
            SecurityAlgorithms.HmacSha256);

        var now = DateTime.UtcNow;
        var token = new JwtSecurityToken(
            issuer: "minidrive",
            audience: "minidrive-api",
            claims: [new Claim("sub", NewUserId())],
            notBefore: now.AddMinutes(-10),
            expires: now.AddMinutes(-1),
            signingCredentials: credentials);

        var client = CreateAnonymousClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));

        var response = await client.GetAsync("/api/v1/nodes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Another_users_node_is_reported_as_not_found_not_forbidden()
    {
        var owner = NewUserId();
        var intruder = NewUserId();

        var ownerClient = CreateClient(owner);
        var folderId = await CreateFolderAsync(ownerClient, $"private-{Guid.NewGuid():N}");

        var intruderClient = CreateClient(intruder);

        foreach (var request in new Func<Task<HttpResponseMessage>>[]
        {
            () => intruderClient.GetAsync($"/api/v1/nodes/{folderId}"),
            () => intruderClient.GetAsync($"/api/v1/nodes/{folderId}/download-url"),
            () => PatchJsonAsync(intruderClient, $"/api/v1/nodes/{folderId}", new { name = "stolen" }),
            () => PostJsonAsync(intruderClient, $"/api/v1/nodes/{folderId}/move", new { parentId = (string?)null }),
            () => intruderClient.DeleteAsync($"/api/v1/nodes/{folderId}"),
        })
        {
            var response = await request();

            // 404 (not 403): the API must not disclose that someone else's node exists.
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

            var problem = await ReadProblemAsync(response);
            Assert.Equal("resource.notfound", problem.Code);
        }
    }

    private sealed record IssuedTokenDto(string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc);
}
