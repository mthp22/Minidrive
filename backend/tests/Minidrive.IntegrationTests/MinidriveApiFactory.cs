using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Minidrive.Application.Abstractions.Storage;

namespace Minidrive.IntegrationTests;

/// <summary>
/// Boots the real API (Program.cs, real MongoDB repositories, real auth pipeline) with object
/// storage replaced by <see cref="FakeObjectStorage"/> and configuration pointed at the
/// disposable database of the current test run.
/// </summary>
/// <remarks>
/// Nothing about this host requires Docker or Cloudflare credentials: R2 settings are syntactically
/// valid placeholders that are never contacted because the storage service is swapped out.
/// </remarks>
public sealed class MinidriveApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;

    public MinidriveApiFactory(string connectionString)
    {
        ArgumentNullException.ThrowIfNull(connectionString);
        _connectionString = connectionString;
        DatabaseName = $"minidrive_tests_{Guid.NewGuid():N}";
    }

    /// <summary>The database this host uses (unique per factory so runs never collide).</summary>
    public string DatabaseName { get; }

    /// <summary>The shared in-memory storage double the API writes through.</summary>
    public FakeObjectStorage Storage { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Real MongoDB, disposable database unique to this factory.
                ["MongoDB:ConnectionString"] = _connectionString,
                ["MongoDB:DatabaseName"] = DatabaseName,
                ["MongoDB:EnsureIndexesOnStartup"] = "true",

                // Syntactically complete R2 settings; never contacted (storage is faked).
                ["CloudflareR2:Endpoint"] = "http://127.0.0.1:1",
                ["CloudflareR2:AccessKeyId"] = "test-access-key",
                ["CloudflareR2:SecretAccessKey"] = "test-secret-key-value-for-tests-only",
                ["CloudflareR2:BucketName"] = "test-bucket",
                ["CloudflareR2:Region"] = "auto",

                // Explicit signing key so tokens survive across factory instances in a test.
                ["Authentication:SigningKey"] = "integration-test-signing-key-0123456789abcdef",
                ["Authentication:Issuer"] = "minidrive",
                ["Authentication:Audience"] = "minidrive-api",
                ["Authentication:EnableDevTokenEndpoint"] = "true",

                // Tests drive maintenance explicitly so results are deterministic.
                ["Maintenance:Enabled"] = "false",

                ["Logging:LogLevel:Default"] = "Warning",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IObjectStorageService>();
            services.AddSingleton<IObjectStorageService>(Storage);
        });
    }

    /// <summary>Creates a client whose request headers carry a freshly minted bearer token.</summary>
    public HttpClient CreateAuthenticatedClient(string userId)
    {
        var client = CreateClient();
        var token = MintToken(userId);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer",
            token.AccessToken);
        return client;
    }

    /// <summary>Mints a token through the same service the API uses, bypassing HTTP.</summary>
    public Minidrive.Api.Authentication.IssuedToken MintToken(string userId) =>
        Services.GetRequiredService<Minidrive.Api.Authentication.IAuthTokenService>().IssueToken(userId);
}
