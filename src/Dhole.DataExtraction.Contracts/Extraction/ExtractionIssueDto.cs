namespace Dhole.DataExtraction.Contracts.Extraction;

public sealed record ExtractionIssueDto(
    Guid Id,
    Guid ExtractionExecutionId,
    Guid? ExtractedPricingRowId,
    string Code,
    string Message,
    bool IsBlocking,
    string? SourceSheetName,
    int? SourceRowNumber,
    string? ColumnName,
    string? RawValue
);
