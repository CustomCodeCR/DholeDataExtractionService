using Dhole.DataExtraction.Domain.Extraction.Entities;

namespace Dhole.DataExtraction.Application.Abstractions.Extraction;

public interface IPricingCatalogStandardizer
{
    Task StandardizeAsync(
        IReadOnlyCollection<PricingExtractionRecord> records,
        Guid? updatedBy = null,
        CancellationToken cancellationToken = default
    );
}
