using Minidrive.Application.Options;
using Minidrive.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Minidrive.Infrastructure.Maintenance;

/// <summary>
/// Drives the maintenance workflows that complete deferred cleanup: expired upload sessions (which
/// release reserved file names and abort abandoned provider uploads) and queued storage deletions
/// for soft-deleted nodes.
/// </summary>
/// <remarks>
/// MongoDB and object storage share no transaction, so deletion is metadata-first and the objects
/// are removed afterwards. This worker is the "afterwards": it is idempotent, batches its work and
/// survives individual failures — a pass that throws is simply retried at the next interval. When
/// disabled by configuration the worker exits and nothing else in the API is affected.
/// </remarks>
public sealed class MaintenanceBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly MaintenanceOptions _options;
    private readonly ILogger<MaintenanceBackgroundService> _logger;

    public MaintenanceBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<MaintenanceOptions> options,
        ILogger<MaintenanceBackgroundService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Background maintenance is disabled by configuration.");
            return;
        }

        _logger.LogInformation(
            "Background maintenance started (interval {IntervalSeconds}s, batch size {BatchSize}).",
            _options.IntervalSeconds,
            _options.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOnceAsync(stoppingToken);

            try
            {
                await Task.Delay(_options.Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Background maintenance stopped.");
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            // MaintenanceService is scoped, so the worker resolves one per pass.
            using var scope = _scopeFactory.CreateScope();
            var maintenance = scope.ServiceProvider.GetRequiredService<MaintenanceService>();

            await maintenance.ExpireStaleUploadsAsync(_options.BatchSize, cancellationToken);
            await maintenance.ProcessStorageCleanupAsync(_options.BatchSize, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Host shutdown, not a failure.
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Background maintenance pass failed; the next interval retries.");
        }
    }
}
