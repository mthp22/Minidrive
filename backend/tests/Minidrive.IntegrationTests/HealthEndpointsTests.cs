using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Minidrive.IntegrationTests;

/// <summary>Health probes and the OpenAPI document must be reachable without a token.</summary>
public sealed class HealthEndpointsTests : ApiTestBase
{
    public HealthEndpointsTests(IntegrationFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task Health_returns_200_without_authentication()
    {
        var response = await CreateAnonymousClient().GetAsync("/api/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());
        Assert.True(body.RootElement.TryGetProperty("timestampUtc", out _));
    }

    [Fact]
    public async Task Readiness_reports_healthy_when_dependencies_are_available()
    {
        // The fixture starts a real mongod; R2 settings are syntactically complete (the storage
        // implementation is faked but the health check only validates configuration).
        var response = await CreateAnonymousClient().GetAsync("/api/v1/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", body.RootElement.GetProperty("status").GetString());

        var checks = body.RootElement.GetProperty("checks");
        Assert.Equal("Healthy", checks.GetProperty("mongodb").GetProperty("status").GetString());
        Assert.Equal("Healthy", checks.GetProperty("object_storage").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Readiness_alias_path_is_available_for_probes()
    {
        var response = await CreateAnonymousClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task OpenApi_document_is_served_with_a_bearer_security_scheme()
    {
        var response = await CreateAnonymousClient().GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Minidrive API", body.RootElement.GetProperty("info").GetProperty("title").GetString());
        Assert.True(
            body.RootElement
                .GetProperty("components")
                .GetProperty("securitySchemes")
                .TryGetProperty("bearer", out _),
            "The generated document must describe the bearer security scheme.");
    }

    [Fact]
    public async Task Protected_endpoint_requires_a_token()
    {
        var response = await CreateAnonymousClient().GetAsync("/api/v1/nodes");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
