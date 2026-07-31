using CustomCodeFramework.Messaging.DependencyInjection;
using CustomCodeFramework.Messaging.Outbox.DependencyInjection;
using CustomCodeFramework.Redis.Streams.DependencyInjection;
using CustomCodeFramework.Workers.DependencyInjection;
using Dhole.DataExtraction.Infrastructure.DependencyInjection;
using Dhole.DataExtraction.Workers.Health;
using Dhole.DataExtraction.Workers.Outbox;
using Dhole.DataExtraction.Workers.Streams;
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
        services.AddCustomCodeRedisStreams(configuration);
        services.AddDataExtractionMessaging(configuration);
        services.AddDataExtractionStreamHandlers();
        services
            .AddHealthChecks()
            .AddCheck<AsyncEmailBacklogHealthCheck>(
                "data-extraction-async-email"
            );

        services.AddCustomCodeWorkers(configuration);
        services.AddCustomCodePeriodicWorker<DataExtractionCacheWarmupWorker>();

        var emailIngestionEnabled = bool.TryParse(
            configuration["EmailIngestion:Enabled"],
            out var configuredEmailIngestionEnabled
        ) && configuredEmailIngestionEnabled;

        if (emailIngestionEnabled)
        {
            services.AddCustomCodePeriodicWorker<EmailPollingWorker>();

            var asyncEmailEnabled = !bool.TryParse(
                configuration["AI:AsyncEmail:Enabled"],
                out var configuredAsyncEmailEnabled
            ) || configuredAsyncEmailEnabled;

            if (asyncEmailEnabled)
            {
                services.AddCustomCodePeriodicWorker<EmailExtractionWorker>();
            }
            else
            {
                services.AddCustomCodePeriodicWorker<LegacyEmailExtractionWorker>();
            }
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

    private static IServiceCollection AddDataExtractionMessaging(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddCustomCodeMessaging(configuration);
        services.AddCustomCodeMessagingOutbox(configuration);
        services.AddCustomCodeOutboxProcessor<OutboxProcessor>();
        services.AddCustomCodeInboxProcessor<InboxProcessor>();
        services.AddCustomCodeMessagingOutboxHostedServices();
        services.AddCustomCodeRedisStreamConsumerBackgroundService();

        return services;
    }

    private static IServiceCollection AddDataExtractionStreamHandlers(
        this IServiceCollection services
    )
    {
        services.AddCustomCodeRedisStreamHandler<ExtractionExecutionCompletedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<ExtractionExecutionFailedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<AiPricingEmailAnalysisStartedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<AiPricingEmailAnalysisCompletedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<AiPricingEmailAnalysisFailedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<PricingImportFromExtractionCompletedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<PricingImportFromExtractionFailedStreamHandler>();

        return services;
    }
}
