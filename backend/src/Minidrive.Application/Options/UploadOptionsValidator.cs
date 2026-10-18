using Microsoft.Extensions.Options;

namespace Minidrive.Application.Options;

/// <summary>
/// Cross-field validation for <see cref="UploadOptions"/>. Registered with <c>ValidateOnStart</c>
/// so a misconfigured deployment fails fast at startup rather than on the first upload.
/// </summary>
public sealed class UploadOptionsValidator : IValidateOptions<UploadOptions>
{
    public ValidateOptionsResult Validate(string? name, UploadOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.ChunkSizeBytes < UploadOptions.MinimumPartSizeBytes)
        {
            failures.Add(
                $"Uploads:ChunkSizeBytes must be at least {UploadOptions.MinimumPartSizeBytes} bytes " +
                "(the provider minimum for non-final parts).");
        }

        if (options.MaxFileSizeBytes <= 0)
        {
            failures.Add("Uploads:MaxFileSizeBytes must be greater than zero.");
        }
        else if (options.MaxFileSizeBytes > UploadOptions.ProviderMaxObjectSizeBytes)
        {
            failures.Add("Uploads:MaxFileSizeBytes exceeds the provider maximum object size.");
        }

        if (options.PresignedUrlExpirationMinutes is < 1 or > 10080)
        {
            failures.Add("Uploads:PresignedUrlExpirationMinutes must be between 1 and 10080 (7 days).");
        }

        if (options.UploadSessionLifetimeHours is < 1 or > 720)
        {
            failures.Add("Uploads:UploadSessionLifetimeHours must be between 1 and 720.");
        }

        if (options.QuotaBytesPerUser < 0)
        {
            failures.Add("Uploads:QuotaBytesPerUser cannot be negative.");
        }

        if (options.MaxFileSizeBytes > 0 && options.ChunkSizeBytes > 0)
        {
            var requiredParts = (options.MaxFileSizeBytes + options.ChunkSizeBytes - 1) / options.ChunkSizeBytes;
            if (requiredParts > UploadOptions.ProviderMaxPartCount)
            {
                failures.Add(
                    $"Uploads:MaxFileSizeBytes requires {requiredParts} parts at the configured chunk size, " +
                    $"which exceeds the provider limit of {UploadOptions.ProviderMaxPartCount}. " +
                    "Increase Uploads:ChunkSizeBytes or reduce Uploads:MaxFileSizeBytes.");
            }
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
