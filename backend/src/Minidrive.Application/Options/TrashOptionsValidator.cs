using Microsoft.Extensions.Options;

namespace Minidrive.Application.Options;

/// <summary>Startup validation for <see cref="TrashOptions"/> so a bad deployment fails fast.</summary>
public sealed class TrashOptionsValidator : IValidateOptions<TrashOptions>
{
    public ValidateOptionsResult Validate(string? name, TrashOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.RetentionDays is < TrashOptions.MinimumRetentionDays or > TrashOptions.MaximumRetentionDays)
        {
            failures.Add(
                $"Trash:RetentionDays must be between {TrashOptions.MinimumRetentionDays} " +
                $"and {TrashOptions.MaximumRetentionDays}.");
        }

        if (options.MaxItems is < 1 or > 10_000)
        {
            failures.Add("Trash:MaxItems must be between 1 and 10000.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
