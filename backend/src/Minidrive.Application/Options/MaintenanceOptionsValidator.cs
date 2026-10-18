using Microsoft.Extensions.Options;

namespace Minidrive.Application.Options;

/// <summary>Startup validation for <see cref="MaintenanceOptions"/> so a bad deployment fails fast.</summary>
public sealed class MaintenanceOptionsValidator : IValidateOptions<MaintenanceOptions>
{
    public ValidateOptionsResult Validate(string? name, MaintenanceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.IntervalSeconds < MaintenanceOptions.MinimumIntervalSeconds)
        {
            failures.Add($"Maintenance:IntervalSeconds must be at least {MaintenanceOptions.MinimumIntervalSeconds}.");
        }

        if (options.BatchSize is < 1 or > MaintenanceOptions.MaximumBatchSize)
        {
            failures.Add($"Maintenance:BatchSize must be between 1 and {MaintenanceOptions.MaximumBatchSize}.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
