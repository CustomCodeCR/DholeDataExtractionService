using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Contracts.Extraction;

namespace Dhole.DataExtraction.Application.Extraction.ExtractPricingData;

public sealed class ExtractPricingDataCommandHandler(IExtractionPipeline pipeline)
    : ICommandHandler<ExtractPricingDataCommand, Result<ExtractPricingDataResponse>>
{
    public async Task<Result<ExtractPricingDataResponse>> HandleAsync(
        ExtractPricingDataCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var response = await pipeline.ExtractPricingDataAsync(command.Request, cancellationToken);

        return response.Success
            ? Result.Success(response)
            : Result.Failure<ExtractPricingDataResponse>(
                new CustomCodeFramework.Core.Results.Error(
                    response.ErrorCode ?? "DataExtraction.ExtractionFailed",
                    response.ErrorMessage ?? "No fue posible extraer los datos del archivo."
                )
            );
    }
}
