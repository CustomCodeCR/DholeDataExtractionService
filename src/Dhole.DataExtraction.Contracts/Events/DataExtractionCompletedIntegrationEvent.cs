namespace Dhole.DataExtraction.Contracts.Events;

public sealed record DataExtractionCompletedIntegrationEvent(
    Guid ExtractionExecutionId,
    Guid PricingImportId,
    string CorrelationId,
    string OriginalFileName,
    string FileHash,
    string SourceFileType,
    int TotalRows,
    int ValidRows,
    int WarningRows,
    int InvalidRows,
    bool HasIssues,
    DateTime OccurredAt
);
