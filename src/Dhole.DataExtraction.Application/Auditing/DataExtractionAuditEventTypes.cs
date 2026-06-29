namespace Dhole.DataExtraction.Application.Auditing;

public static class DataExtractionAuditEventTypes
{
    public const string ExtractionExecutionStarted = "data-extraction.execution.started";

    public const string ExtractionExecutionCompleted = "data-extraction.execution.completed";

    public const string ExtractionExecutionFailed = "data-extraction.execution.failed";

    public const string SourceDocumentReceived = "data-extraction.source-document.received";

    public const string PricingRecordsExtracted = "data-extraction.pricing-records.extracted";

    public const string ExtractionIssuesDetected = "data-extraction.issues.detected";

    public const string FileStructureDetected = "data-extraction.file-structure.detected";

    public const string ColumnMappingPreviewed = "data-extraction.column-mapping.previewed";

    public const string PricingDataValidated = "data-extraction.pricing-data.validated";
}
