using CustomCodeFramework.Core.Domain.Events;
using Dhole.DataExtraction.Domain.Extraction.Events;

namespace Dhole.DataExtraction.Persistence.Messaging;

internal static class DomainEventOutboxMapper
{
    public static string GetEventName(IDomainEvent domainEvent)
    {
        return domainEvent switch
        {
            ExtractionExecutionStartedDomainEvent => "data-extraction.execution.started",

            ExtractionExecutionCompletedDomainEvent => "data-extraction.execution.completed",

            ExtractionExecutionFailedDomainEvent => "data-extraction.execution.failed",

            _ => $"data-extraction.{domainEvent.GetType().Name}",
        };
    }

    public static string GetEventType(IDomainEvent domainEvent)
    {
        return domainEvent.GetType().FullName ?? domainEvent.GetType().Name;
    }
}
