using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Minidrive.Api.Endpoints;

/// <summary>
/// Health endpoints. Liveness reports process health only; readiness requires every registered
/// dependency check (MongoDB and object storage configuration) to pass.
/// </summary>
public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Liveness: deliberately touches no dependency. A failing database must not make the
        // orchestrator restart the process.
        endpoints.MapGet("/api/v1/health", () =>
            Results.Ok(new { status = "Healthy", timestampUtc = DateTimeOffset.UtcNow }))
            .WithName("GetLiveness")
            .WithTags("Health")
            .WithSummary("Liveness probe: reports that the process is running.")
            .AllowAnonymous();

        var options = new HealthCheckOptions
        {
            Predicate = _ => true,
            ResponseWriter = WriteReportAsync,
        };

        endpoints.MapHealthChecks("/api/v1/health/ready", options)
            .WithName("GetReadiness")
            .WithTags("Health")
            .WithSummary("Readiness probe: reports whether MongoDB and object storage are usable.")
            .AllowAnonymous();

        // Alias for orchestrators that probe the conventional /health/ready path.
        endpoints.MapHealthChecks("/health/ready", options)
            .WithTags("Health")
            .AllowAnonymous();

        return endpoints;
    }

    /// <summary>
    /// Health check descriptions are intentionally generic: connection details, hosts and topology
    /// are never returned to the caller.
    /// </summary>
    private static Task WriteReportAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";

        return context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new { status = entry.Value.Status.ToString(), description = entry.Value.Description }),
        });
    }
}
