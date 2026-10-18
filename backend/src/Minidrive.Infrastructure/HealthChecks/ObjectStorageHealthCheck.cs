using Minidrive.Infrastructure.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Minidrive.Infrastructure.HealthChecks;

/// <summary>
/// Readiness check for object storage. It verifies that the R2 configuration is complete and that a
/// usable endpoint can be resolved.
/// </summary>
/// <remarks>
/// This deliberately performs a configuration check rather than a network round-trip: readiness
/// probes run frequently, and issuing an authenticated upstream request on every probe would add
/// cost and load. No secret values are ever included in the result.
/// </remarks>
public sealed class ObjectStorageHealthCheck : IHealthCheck
{
    private readonly CloudflareR2Options _options;

    public ObjectStorageHealthCheck(IOptions<CloudflareR2Options> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;

        var endpointResolved = _options.ResolveEndpoint() is not null;
        var credentialsPresent = !string.IsNullOrWhiteSpace(_options.AccessKeyId)
            && !string.IsNullOrWhiteSpace(_options.SecretAccessKey);
        var bucketPresent = !string.IsNullOrWhiteSpace(_options.BucketName);

        if (endpointResolved && credentialsPresent && bucketPresent)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Object storage is configured."));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy("Object storage is not fully configured."));
    }
}
