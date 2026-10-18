using Minidrive.Infrastructure.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Minidrive.Infrastructure.Persistence;

/// <summary>
/// Runs <see cref="IMongoIndexInitializer"/> during application startup. Startup is retried briefly
/// to tolerate a database that is still accepting connections, then fails fast if indexes cannot be
/// created: serving traffic against an unindexed or incorrectly constrained collection is worse than
/// refusing to start.
/// </summary>
internal sealed class MongoIndexInitializerHostedService : IHostedService
{
    private const int MaxAttempts = 5;

    private readonly IMongoIndexInitializer _indexInitializer;
    private readonly MongoDbOptions _options;
    private readonly ILogger<MongoIndexInitializerHostedService> _logger;

    public MongoIndexInitializerHostedService(
        IMongoIndexInitializer indexInitializer,
        IOptions<MongoDbOptions> options,
        ILogger<MongoIndexInitializerHostedService> logger)
    {
        _indexInitializer = indexInitializer;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.EnsureIndexesOnStartup)
        {
            _logger.LogInformation("MongoDB index initialization is disabled by configuration.");
            return;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await _indexInitializer.EnsureIndexesAsync(cancellationToken);
                return;
            }
            catch (Exception exception) when (attempt < MaxAttempts)
            {
                _logger.LogWarning(
                    exception,
                    "MongoDB index initialization attempt {Attempt} of {MaxAttempts} failed; retrying.",
                    attempt,
                    MaxAttempts);

                await Task.Delay(TimeSpan.FromMilliseconds(250 * attempt), cancellationToken);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
