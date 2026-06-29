namespace Dhole.DataExtraction.Contracts.Events;

public sealed record DataExtractionFailedIntegrationEvent(
    Guid ExtractionExecutionId,
    Guid PricingImportId,
    string CorrelationId,
    string OriginalFileName,
    string FileHash,
    string SourceFileType,
    string ErrorCode,
    string ErrorMessage,
    DateTime OccurredAt
);
