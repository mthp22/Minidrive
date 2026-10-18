using Minidrive.Application.Options;
using Minidrive.Application.Services;
using Minidrive.Application.Validation;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Minidrive.Application;

/// <summary>Composition root for the application layer.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // Upload configuration is bound and validated at startup so a bad deployment fails fast.
        services.AddOptions<UploadOptions>()
            .Bind(configuration.GetSection(UploadOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<UploadOptions>, UploadOptionsValidator>();

        services.AddOptions<MaintenanceOptions>()
            .Bind(configuration.GetSection(MaintenanceOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<MaintenanceOptions>, MaintenanceOptionsValidator>();

        services.AddValidatorsFromAssemblyContaining<CreateFolderRequestValidator>(ServiceLifetime.Scoped);

        services.TryAddSingleton(TimeProvider.System);

        services.AddScoped<NodeQueryService>();
        services.AddScoped<NodeCommandService>();
        services.AddScoped<UploadService>();
        services.AddScoped<MaintenanceService>();

        return services;
    }
}
