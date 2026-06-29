using CustomCodeFramework.Core.Domain.Events;

namespace Dhole.DataExtraction.Domain.Extraction.Events;

public sealed record ExtractionExecutionCompletedDomainEvent(
    Guid id,
    Guid pricingImportId,
    string correlationId,
    int totalRows,
    int validRows,
    int warningRows,
    int invalidRows
) : DomainEvent;
