namespace Dhole.DataExtraction.Contracts.Extraction;

public sealed record ExtractPricingDataResponse(
    bool Success,
    Guid? ExtractionExecutionId,
    Guid PricingImportId,
    string CorrelationId,
    ExtractionSummaryDto Summary,
    ExtractionSourceDocumentDto? SourceDocument,
    IReadOnlyCollection<ExtractedPricingRowDto> Rows,
    IReadOnlyCollection<ExtractionIssueDto> Issues,
    string? ErrorCode,
    string? ErrorMessage,
    CatalogReferenceDto? ProfileReference = null
);
