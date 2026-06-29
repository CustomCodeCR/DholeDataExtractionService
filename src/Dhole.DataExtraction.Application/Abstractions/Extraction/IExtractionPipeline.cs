using Dhole.DataExtraction.Contracts.Extraction;

namespace Dhole.DataExtraction.Application.Abstractions.Extraction;

public interface IExtractionPipeline
{
    Task<ExtractPricingDataResponse> ExtractPricingDataAsync(
        ExtractionDataRequest request,
        CancellationToken cancellationToken = default
    );
}
