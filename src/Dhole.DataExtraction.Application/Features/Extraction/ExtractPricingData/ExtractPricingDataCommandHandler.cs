using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Contracts.Extraction;

namespace Dhole.DataExtraction.Application.Extraction.ExtractPricingData;

public sealed class ExtractPricingDataCommandHandler(
    IAutomatedPricingExtractionService automatedExtraction
)
    : ICommandHandler<ExtractPricingDataCommand, Result<ExtractPricingDataResponse>>
{
    public async Task<Result<ExtractPricingDataResponse>> HandleAsync(
        ExtractPricingDataCommand command,
        CancellationToken cancellationToken = default
    )
    {
        var automaticResult = await automatedExtraction.ExtractAsync(
            command.Request,
            new AutomatedPricingExtractionContext(
                SourceType: command.Request.SourceOriginType ?? "ManualUpload",
                ForceAiAnalysis: true
            ),
            cancellationToken
        );
        var response = automaticResult.Response;

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
