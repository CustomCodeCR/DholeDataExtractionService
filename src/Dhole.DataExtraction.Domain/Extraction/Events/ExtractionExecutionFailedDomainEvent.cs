using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.DataExtraction.Domain.Extraction.Events;

public sealed record ExtractionExecutionFailedDomainEvent(
    Guid id,
    Guid pricingImportId,
    string correlationId,
    string errorMessage
) : DomainEvent;
