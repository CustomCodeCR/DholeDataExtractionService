using CustomCodeFramework.Workers.DependencyInjection;
using Dhole.DataExtraction.Infrastructure.DependencyInjection;
using Dhole.DataExtraction.Workers.Workers;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Dhole.DataExtraction.Workers.DependencyInjection;

public static class WorkerServiceCollectionExtensions
{
    public static IServiceCollection AddDataExtractionWorker(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddInfrastructure(configuration, includeWebAuthentication: false);

        services.AddCustomCodeWorkers(configuration);
        services.AddCustomCodePeriodicWorker<DataExtractionCacheWarmupWorker>();

        var emailIngestionEnabled = bool.TryParse(
            configuration["EmailIngestion:Enabled"],
            out var configuredEmailIngestionEnabled
        ) && configuredEmailIngestionEnabled;

        if (emailIngestionEnabled)
        {
            services.AddCustomCodePeriodicWorker<EmailPollingWorker>();
            services.AddCustomCodePeriodicWorker<EmailExtractionWorker>();
        }

        services.PostConfigure<HealthCheckServiceOptions>(options =>
        {
            var uniqueRegistrations = options.Registrations
                .GroupBy(registration => registration.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            options.Registrations.Clear();

            foreach (var registration in uniqueRegistrations)
            {
                options.Registrations.Add(registration);
            }
        });

        return services;
    }
}
