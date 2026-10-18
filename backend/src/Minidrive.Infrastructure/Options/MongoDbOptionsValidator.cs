using Microsoft.Extensions.Options;

namespace Minidrive.Infrastructure.Options;

/// <summary>Startup validation for <see cref="MongoDbOptions"/>; never echoes secret values.</summary>
public sealed class MongoDbOptionsValidator : IValidateOptions<MongoDbOptions>
{
    public ValidateOptionsResult Validate(string? name, MongoDbOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add("MongoDB:ConnectionString is required.");
        }

        if (string.IsNullOrWhiteSpace(options.DatabaseName))
        {
            failures.Add("MongoDB:DatabaseName is required.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
