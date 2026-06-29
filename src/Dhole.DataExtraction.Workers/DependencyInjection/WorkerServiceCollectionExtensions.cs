using CustomCodeFramework.Messaging.DependencyInjection;
using CustomCodeFramework.Messaging.Outbox.DependencyInjection;
using CustomCodeFramework.Redis.DependencyInjection;
using CustomCodeFramework.Redis.Streams.DependencyInjection;
using CustomCodeFramework.Workers.DependencyInjection;
using Dhole.DataExtraction.Application.Abstractions.Cache;
using Dhole.DataExtraction.Infrastructure.Cache;
using Dhole.DataExtraction.Workers.Outbox;
using Dhole.DataExtraction.Workers.Streams;
using Dhole.DataExtraction.Workers.Workers;

namespace Dhole.DataExtraction.Workers.DependencyInjection;

public static class WorkerServiceCollectionExtensions
{
    public static IServiceCollection AddDataExtractionWorker(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddCustomCodeRedis(configuration);
        services.AddCustomCodeRedisStreams(configuration);
        services.AddScoped<IDataExtractionCacheService, DataExtractionCacheService>();

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

        return services;
    }
}
