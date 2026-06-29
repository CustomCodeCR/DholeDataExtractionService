using Dhole.DataExtraction.Domain.Extraction.Entities;

namespace Dhole.DataExtraction.Application.Abstractions.Extraction;

public interface IDataQualityValidator
{
    Task<ExtractionValidationResult> ValidateAsync(
        Guid extractionExecutionId,
        IReadOnlyCollection<PricingExtractionRecord> records,
        CancellationToken cancellationToken = default
    );
}

public sealed record ExtractionValidationResult(
    int TotalRows,
    int ValidRows,
    int WarningRows,
    int InvalidRows,
    IReadOnlyCollection<ExtractionIssue> Issues
)
{
    public bool HasIssues => WarningRows > 0 || InvalidRows > 0 || Issues.Count > 0;
}
