using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Minidrive.IntegrationTests;

/// <summary>
/// Shared plumbing for API tests: one host (from <see cref="IntegrationFixture"/>), JSON helpers
/// that match the API contract, and per-test user ids so tests can never observe each other's data.
/// </summary>
[Collection(IntegrationCollection.Name)]
public abstract class ApiTestBase
{
    protected ApiTestBase(IntegrationFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        Fixture = fixture;
    }

    protected IntegrationFixture Fixture { get; }

    protected MinidriveApiFactory Factory => Fixture.Factory;

    /// <summary>Serializer settings identical to the ones the API uses (web naming + string enums).</summary>
    protected static JsonSerializerOptions Json { get; } = CreateJsonOptions();

    /// <summary>A brand-new user id: every test runs under its own owner.</summary>
    protected static string NewUserId() => $"user-{Guid.NewGuid():N}";

    /// <summary>An anonymous client (no bearer token).</summary>
    protected HttpClient CreateAnonymousClient() => Factory.CreateClient();

    /// <summary>A client authenticated as <paramref name="userId"/>.</summary>
    protected HttpClient CreateClient(string userId) => Factory.CreateAuthenticatedClient(userId);

    protected Task<HttpResponseMessage> PostJsonAsync<TBody>(HttpClient client, string url, TBody body) =>
        client.PostAsJsonAsync(url, body, Json);

    protected Task<HttpResponseMessage> PatchJsonAsync<TBody>(HttpClient client, string url, TBody body) =>
        client.PatchAsJsonAsync(url, body, Json);

    protected async Task<TBody?> ReadAsync<TBody>(HttpResponseMessage response) where TBody : class =>
        await response.Content.ReadFromJsonAsync<TBody>(Json);

    /// <summary>Asserts a Problem Details body and returns it for further assertions.</summary>
    protected async Task<ApiProblem> ReadProblemAsync(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<ApiProblem>(Json);
        Assert.NotNull(problem);
        return problem;
    }

    /// <summary>Creates a folder through the API and returns its id.</summary>
    protected async Task<string> CreateFolderAsync(HttpClient client, string name, string? parentId = null)
    {
        var response = await PostJsonAsync(client, "/api/v1/folders", new { parentId, name });
        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);

        var folder = await ReadAsync<Minidrive.Application.Models.NodeDetails>(response);
        Assert.NotNull(folder);
        return folder.Id;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}

/// <summary>Minimal Problem Details shape, including the API's stable <c>code</c> extension.</summary>
public sealed record ApiProblem(
    int? Status,
    string? Title,
    string? Detail,
    string? Type,
    string? Instance,
    string? Code,
    Dictionary<string, string[]>? Errors);
