using CustomCodeFramework.Core.Domain.Events;
using Dhole.DataExtraction.Domain.Extraction.Enums;

namespace Dhole.DataExtraction.Domain.Extraction.Events;

public sealed record ExtractionExecutionStartedDomainEvent(
    Guid id,
    Guid pricingImportId,
    string correlationId,
    string originalFileName,
    SourceFileType sourceFileType,
    Guid? requestedBy
) : DomainEvent;
