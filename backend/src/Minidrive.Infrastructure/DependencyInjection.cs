using Amazon.S3;
using Minidrive.Application.Abstractions;
using Minidrive.Application.Abstractions.Persistence;
using Minidrive.Application.Abstractions.Storage;
using Minidrive.Infrastructure.Options;
using Minidrive.Infrastructure.Persistence;
using Minidrive.Infrastructure.Maintenance;
using Minidrive.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Minidrive.Infrastructure;

/// <summary>Composition root for the infrastructure layer.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Configuration is bound and validated at startup so a bad deployment fails fast.
        services.AddOptions<MongoDbOptions>()
            .Bind(configuration.GetSection(MongoDbOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MongoDbOptions>, MongoDbOptionsValidator>();

        services.AddOptions<CloudflareR2Options>()
            .Bind(configuration.GetSection(CloudflareR2Options.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<CloudflareR2Options>, CloudflareR2OptionsValidator>();

        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<IMongoClient>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<MongoDbOptions>>().Value;
            return new MongoClient(options.ConnectionString);
        });

        services.TryAddSingleton<IMongoDatabase>(provider =>
        {
            var client = provider.GetRequiredService<IMongoClient>();
            var options = provider.GetRequiredService<IOptions<MongoDbOptions>>().Value;
            return client.GetDatabase(options.DatabaseName);
        });

        services.AddSingleton<IAmazonS3>(provider =>
            R2ClientFactory.Create(provider.GetRequiredService<IOptions<CloudflareR2Options>>().Value));

        services.AddSingleton<IIdGenerator, ObjectIdGenerator>();
        services.AddSingleton<INodeRepository, MongoNodeRepository>();
        services.AddSingleton<ITrashPurgeStore, MongoTrashPurgeStore>();
        services.AddSingleton<IUploadSessionRepository, MongoUploadSessionRepository>();
        services.AddSingleton<IStorageCleanupQueue, MongoStorageCleanupQueue>();
        services.AddSingleton<IObjectStorageService, R2ObjectStorageService>();

        services.AddSingleton<IMongoIndexInitializer, MongoIndexInitializer>();
        services.AddHostedService<MongoIndexInitializerHostedService>();

        // Deferred cleanup: expiring abandoned uploads and deleting orphaned storage objects.
        services.AddHostedService<MaintenanceBackgroundService>();

        return services;
    }
}
