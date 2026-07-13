using CustomCodeFramework.Messaging.DependencyInjection;
using CustomCodeFramework.Messaging.Outbox.DependencyInjection;
using CustomCodeFramework.Redis.Streams.DependencyInjection;
using CustomCodeFramework.Workers.DependencyInjection;
using Dhole.DataExtraction.Infrastructure.DependencyInjection;
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

        services.AddCustomCodeMessaging(configuration);
        services.AddCustomCodeMessagingOutbox(configuration);
        services.AddCustomCodeOutboxProcessor<OutboxProcessor>();
        services.AddCustomCodeInboxProcessor<InboxProcessor>();
        services.AddCustomCodeMessagingOutboxHostedServices();

        services.AddCustomCodeRedisStreamConsumerBackgroundService();
        services.AddCustomCodeRedisStreamHandler<ExtractionExecutionCompletedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<ExtractionExecutionFailedStreamHandler>();

        services.AddCustomCodeWorkers(configuration);
        services.AddCustomCodePeriodicWorker<DataExtractionCacheWarmupWorker>();
        services.AddCustomCodePeriodicWorker<EmailPollingWorker>();
        services.AddCustomCodePeriodicWorker<EmailExtractionWorker>();

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
