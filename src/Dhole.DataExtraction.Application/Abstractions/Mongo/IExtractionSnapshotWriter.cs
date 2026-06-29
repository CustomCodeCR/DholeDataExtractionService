using Dhole.DataExtraction.Contracts.Extraction;

namespace Dhole.DataExtraction.Application.Abstractions.Mongo;

public interface IExtractionSnapshotWriter
{
    Task WriteAsync(
        Guid eventId,
        string eventName,
        Guid extractionExecutionId,
        Guid pricingImportId,
        string correlationId,
        string originalFileName,
        string fileHash,
        string sourceFileType,
        ExtractionSummaryDto summary,
        IReadOnlyCollection<ExtractedPricingRowDto> rows,
        IReadOnlyCollection<ExtractionIssueDto> issues,
        Guid? executedBy,
        DateTime occurredAtUtc,
        CancellationToken cancellationToken = default
    );
}
