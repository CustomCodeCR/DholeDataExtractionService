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
        var maxRetryCount = ReadPositiveInt(
            configuration["Messaging:Outbox:MaxRetryCount"],
            3
        );

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
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested
            )
            {
                throw;
            }
            catch (Exception exception)
            {
                message.RetryCount++;
                var exhausted = message.RetryCount >= maxRetryCount;
                message.Status = exhausted
                    ? OutboxMessageStatus.Failed
                    : OutboxMessageStatus.Pending;
                message.ErrorMessage = Limit(exception.Message, 4000);
                failedCount++;

                logger.Log(
                    exhausted ? LogLevel.Error : LogLevel.Warning,
                    exception,
                    "Failed to publish data extraction outbox message {EventId}. "
                        + "Attempt {RetryCount}/{MaxRetryCount}; terminal: {Exhausted}.",
                    message.EventId,
                    message.RetryCount,
                    maxRetryCount,
                    exhausted
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

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }

    private static string Limit(string value, int maximumLength)
    {
        return value.Length <= maximumLength ? value : value[..maximumLength];
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
