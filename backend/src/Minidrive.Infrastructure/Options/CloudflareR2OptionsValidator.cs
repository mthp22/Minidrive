using Microsoft.Extensions.Options;

namespace Minidrive.Infrastructure.Options;

/// <summary>
/// Startup validation for <see cref="CloudflareR2Options"/>. Failures name the missing setting only;
/// secret values are never included in the message.
/// </summary>
public sealed class CloudflareR2OptionsValidator : IValidateOptions<CloudflareR2Options>
{
    public ValidateOptionsResult Validate(string? name, CloudflareR2Options options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.AccessKeyId))
        {
            failures.Add("CloudflareR2:AccessKeyId is required.");
        }

        if (string.IsNullOrWhiteSpace(options.SecretAccessKey))
        {
            failures.Add("CloudflareR2:SecretAccessKey is required.");
        }

        if (string.IsNullOrWhiteSpace(options.BucketName))
        {
            failures.Add("CloudflareR2:BucketName is required.");
        }

        if (options.ResolveEndpoint() is null)
        {
            failures.Add("CloudflareR2:AccountId or CloudflareR2:Endpoint is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Region))
        {
            failures.Add("CloudflareR2:Region is required.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
