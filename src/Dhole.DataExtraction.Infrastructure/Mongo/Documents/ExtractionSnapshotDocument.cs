using Dhole.DataExtraction.Contracts.Extraction;

namespace Dhole.DataExtraction.Infrastructure.Mongo.Documents;

public sealed record ExtractionSnapshotDocument(
    Guid Id,
    Guid ExtractionExecutionId,
    Guid PricingImportId,
    string CorrelationId,
    string OriginalFileName,
    string FileHash,
    string SourceFileType,
    ExtractionSummaryDto Summary,
    IReadOnlyCollection<ExtractedPricingRowDto> Rows,
    IReadOnlyCollection<ExtractionIssueDto> Issues,
    DateTime OccurredAtUtc
);
