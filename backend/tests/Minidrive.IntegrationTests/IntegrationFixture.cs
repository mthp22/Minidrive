using EphemeralMongo;
using Xunit;

namespace Minidrive.IntegrationTests;

/// <summary>
/// Defines the <c>Integration</c> collection: every test class in it shares one disposable MongoDB
/// server, so the binaries are downloaded/started once per test run instead of once per class.
/// </summary>
[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<IntegrationFixture>
{
    public const string Name = "Integration";
}

/// <summary>
/// Starts a real <c>mongod</c> process for the test run and owns the single API host under test.
/// </summary>
/// <remarks>
/// EphemeralMongo downloads the MongoDB binaries on first use (network required once, cached
/// afterwards) and deletes the data directory on dispose. No Docker is involved.
/// </remarks>
public sealed class IntegrationFixture : IAsyncLifetime
{
    private IMongoRunner? _runner;

    /// <summary>Connection string of the disposable MongoDB server.</summary>
    public string ConnectionString { get; private set; } = string.Empty;

    /// <summary>The API host shared by every test class in the collection.</summary>
    public MinidriveApiFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _runner = await MongoRunner.RunAsync(
            new MongoRunnerOptions
            {
                ConnectionTimeout = TimeSpan.FromSeconds(60),
                DataDirectoryLifetime = TimeSpan.FromHours(1),
            });

        ConnectionString = _runner.ConnectionString;
        Factory = new MinidriveApiFactory(ConnectionString);

        // Force the host (and index initialisation) to start now, so failures surface here.
        Factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        _runner?.Dispose();
    }
}
