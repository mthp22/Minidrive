using Amazon.Runtime;
using Amazon.S3;
using Minidrive.Infrastructure.Options;

namespace Minidrive.Infrastructure.Storage;

/// <summary>Creates the S3-compatible client used to talk to Cloudflare R2.</summary>
internal static class R2ClientFactory
{
    /// <summary>
    /// Builds an S3 client configured for R2. Path-style addressing is required by the R2 endpoint,
    /// and the signing region is <c>auto</c> unless overridden.
    /// </summary>
    public static IAmazonS3 Create(CloudflareR2Options options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var endpoint = options.ResolveEndpoint()
            ?? throw new InvalidOperationException("A Cloudflare R2 endpoint could not be resolved from configuration.");

        var config = new AmazonS3Config
        {
            ServiceURL = endpoint,
            ForcePathStyle = options.ForcePathStyle,
            AuthenticationRegion = options.Region,
        };

        return new AmazonS3Client(new BasicAWSCredentials(options.AccessKeyId, options.SecretAccessKey), config);
    }
}
