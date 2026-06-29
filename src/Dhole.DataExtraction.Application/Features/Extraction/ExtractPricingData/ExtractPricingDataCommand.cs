using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Commands;
using Dhole.DataExtraction.Contracts.Extraction;

namespace Dhole.DataExtraction.Application.Extraction.ExtractPricingData;

public sealed record ExtractPricingDataCommand(ExtractionDataRequest Request)
    : ICommand<Result<ExtractPricingDataResponse>>;
