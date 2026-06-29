using System.Text.Json;
using CustomCodeFramework.Messaging.Outbox;
using Dhole.DataExtraction.Application.Abstractions.Messaging;
using Dhole.DataExtraction.Persistence.DbContexts;

namespace Dhole.DataExtraction.Persistence.Messaging;

public sealed class IntegrationEventOutboxWriter(ServiceDbContext dbContext)
    : IIntegrationEventOutboxWriter
{
    public async Task WriteAsync(
        string eventType,
        string eventName,
        object payload,
        string? correlationId = null,
        CancellationToken cancellationToken = default
    )
    {
        var message = new OutboxMessage
        {
            EventId = Guid.NewGuid(),
            EventType = eventType,
            EventName = eventName,
            SourceService = "DholeDataExtractionService",
            PayloadJson = JsonSerializer.Serialize(payload),
            HeadersJson = null,
            CorrelationId = correlationId,
            Status = OutboxMessageStatus.Pending,
            RetryCount = 0,
            ErrorMessage = null,
            CreatedAtUtc = DateTime.UtcNow,
        };

        await dbContext.OutboxMessages.AddAsync(message, cancellationToken);
    }
}
