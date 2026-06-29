namespace Dhole.DataExtraction.Contracts.Extraction;

public sealed record ExtractionSummaryDto(
    int TotalRows,
    int ValidRows,
    int WarningRows,
    int InvalidRows,
    bool HasIssues
);
