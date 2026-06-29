namespace Dhole.DataExtraction.Application.Auditing;

public static class DataExtractionAuditActions
{
    public const string Started = "started";
    public const string Completed = "completed";
    public const string Failed = "failed";

    public const string DocumentReceived = "document_received";
    public const string RowsExtracted = "rows_extracted";
    public const string IssuesDetected = "issues_detected";

    public const string Previewed = "previewed";
    public const string Validated = "validated";
}
