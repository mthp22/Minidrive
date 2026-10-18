using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Minidrive.Infrastructure.HealthChecks;

/// <summary>
/// Readiness check for MongoDB. It issues a <c>ping</c> command so the result reflects the database
/// dependency rather than mere process uptime.
/// </summary>
/// <remarks>
/// Failure descriptions are deliberately generic: connection strings, hosts and topology details are
/// never returned to callers of the readiness endpoint.
/// </remarks>
public sealed class MongoHealthCheck : IHealthCheck
{
    private readonly IMongoDatabase _database;
    private readonly ILogger<MongoHealthCheck> _logger;

    public MongoHealthCheck(IMongoDatabase database, ILogger<MongoHealthCheck> logger)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(logger);

        _database = database;
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _database.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1),
                cancellationToken: cancellationToken);

            return HealthCheckResult.Healthy("MongoDB is reachable.");
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "MongoDB readiness check failed.");
            return HealthCheckResult.Unhealthy("MongoDB is not reachable.");
        }
    }
}
