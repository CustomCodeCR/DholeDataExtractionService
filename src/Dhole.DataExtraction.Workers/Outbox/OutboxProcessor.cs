using System.Text.Json;
using CustomCodeFramework.Messaging.Outbox;
using CustomCodeFramework.Messaging.Outbox.Processing;
using CustomCodeFramework.Redis.Streams.Abstractions;
using CustomCodeFramework.Redis.Streams.Messages;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Workers.Outbox;

internal sealed class OutboxProcessor(
    ServiceDbContext dbContext,
    IRedisStreamPublisher redisStreamPublisher,
    IConfiguration configuration,
    ILogger<OutboxProcessor> logger
) : IOutboxProcessor
{
    public async Task<OutboxProcessingResult> ProcessAsync(
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        var messages = await dbContext
            .OutboxMessages.Where(x => x.Status == OutboxMessageStatus.Pending)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return OutboxProcessingResult.Empty;
        }

        var processedCount = 0;
        var failedCount = 0;

        foreach (var message in messages)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<object>(message.PayloadJson);
                var streamName = ResolveStreamName(message.EventName);

                await redisStreamPublisher.PublishAsync(
                    new RedisStreamMessage
                    {
                        StreamName = streamName,
                        MessageType = message.EventName,
                        Payload = payload ?? message.PayloadJson,
                        Headers = CreateHeaders(message),
                    },
                    cancellationToken
                );

                message.Status = OutboxMessageStatus.Processed;
                message.ProcessedAtUtc = DateTime.UtcNow;
                message.ErrorMessage = null;
                processedCount++;
            }
            catch (Exception exception)
            {
                message.RetryCount++;
                message.Status = OutboxMessageStatus.Failed;
                message.ErrorMessage = exception.Message;
                failedCount++;

                logger.LogError(
                    exception,
                    "Failed to publish data extraction outbox message {EventId}.",
                    message.EventId
                );
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var hasMoreMessages = await dbContext.OutboxMessages.AnyAsync(
            x => x.Status == OutboxMessageStatus.Pending,
            cancellationToken
        );

        return new OutboxProcessingResult(processedCount, failedCount, hasMoreMessages);
    }

    private string ResolveStreamName(string eventName)
    {
        return configuration[$"Redis:Streams:Destinations:{eventName}"]
            ?? configuration["Redis:Streams:DefaultStreamName"]
            ?? "dhole.data-extraction.events";
    }

    private static Dictionary<string, string> CreateHeaders(OutboxMessage message)
    {
        var headers = new Dictionary<string, string>
        {
            ["event_id"] = message.EventId.ToString(),
            ["event_type"] = message.EventType,
            ["event_name"] = message.EventName,
            ["source_service"] = message.SourceService,
            ["created_at_utc"] = message.CreatedAtUtc.ToString("O"),
        };

        if (!string.IsNullOrWhiteSpace(message.CorrelationId))
        {
            headers["correlation_id"] = message.CorrelationId;
        }

        return headers;
    }
}
