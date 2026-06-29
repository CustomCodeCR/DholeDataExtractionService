using System.Text.Json;
using CustomCodeFramework.Messaging.Outbox;
using Dhole.DataExtraction.Application.Abstractions.Auditing;
using Dhole.DataExtraction.Persistence.DbContexts;

namespace Dhole.DataExtraction.Persistence.Auditing;

public sealed class DataExtractionAuditService(ServiceDbContext dbContext)
    : IDataExtractionAuditService
{
    private const string SourceService = "DholeDataExtractionService";
    private const string AuditEventType =
        "Dhole.AuditLogs.Contracts.AuditEvents.RegisterAuditEventRequest";
    private const string AuditEventName = "audit.event.registered";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task PublishAsync(
        DataExtractionAuditEvent auditEvent,
        CancellationToken cancellationToken = default
    )
    {
        var current = AuditExecutionContextAccessor.Current;
        var eventId = auditEvent.EventId ?? Guid.NewGuid();
        var correlationId = current?.CorrelationId ?? Guid.NewGuid();
        var occurredAt = auditEvent.OccurredAt ?? DateTime.UtcNow;

        var payload = new
        {
            EventId = eventId,
            CorrelationId = correlationId,
            SourceService,
            auditEvent.EntityType,
            auditEvent.EntityId,
            auditEvent.Action,
            auditEvent.EventType,
            UserId = auditEvent.ActorUserId ?? current?.UserId,
            UserName = auditEvent.ActorUserName ?? current?.UserName,
            IpAddress = current?.IpAddress,
            UserAgent = current?.UserAgent,
            OccurredAt = occurredAt,
            BeforeJson = SerializeNullable(auditEvent.Before),
            AfterJson = SerializeNullable(auditEvent.After),
            PayloadJson = SerializeNullable(auditEvent.Payload),
            Metadata = SerializeNullable(auditEvent.Metadata),
            auditEvent.ErrorMessage,
            StackTrace = (string?)null,
            Details = Array.Empty<object>(),
        };

        dbContext.OutboxMessages.Add(
            new OutboxMessage
            {
                EventId = Guid.NewGuid(),
                EventType = AuditEventType,
                EventName = AuditEventName,
                SourceService = SourceService,
                PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
                HeadersJson = null,
                CorrelationId = correlationId.ToString(),
                Status = OutboxMessageStatus.Pending,
                RetryCount = 0,
                ErrorMessage = null,
                CreatedAtUtc = DateTime.UtcNow,
            }
        );

        return Task.CompletedTask;
    }

    private static string? SerializeNullable(object? value)
    {
        if (value is null)
        {
            return null;
        }

        return value is string text ? text : JsonSerializer.Serialize(value, JsonOptions);
    }
}
