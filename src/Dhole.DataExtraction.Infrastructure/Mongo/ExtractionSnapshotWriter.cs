using Dhole.DataExtraction.Application.Abstractions.Mongo;
using Dhole.DataExtraction.Contracts.Extraction;

namespace Dhole.DataExtraction.Infrastructure.Mongo;

public sealed class ExtractionSnapshotWriter : IExtractionSnapshotWriter
{
    public Task WriteAsync(
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
    )
    {
        return Task.CompletedTask;
    }
}
