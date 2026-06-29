using Dhole.DataExtraction.Domain.Extraction.Entities;

namespace Dhole.DataExtraction.Application.Abstractions.Extraction;

public interface IPricingRecordNormalizer
{
    Task<IReadOnlyCollection<PricingExtractionRecord>> NormalizeAsync(
        Guid extractionExecutionId,
        Guid sourceDocumentId,
        IReadOnlyCollection<MappedPricingRow> rows,
        Guid? createdBy = null,
        CancellationToken cancellationToken = default
    );
}
