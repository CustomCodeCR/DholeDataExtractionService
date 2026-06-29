using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.DataExtraction.Contracts.Extraction;

namespace Dhole.DataExtraction.Application.Extraction.ValidatePricingData;

public sealed record ValidatePricingDataQuery(
    string OriginalFileName,
    string? ContentType,
    byte[] FileContent,
    string? ProfileCode,
    Guid? RequestedBy
) : IQuery<Result<ValidatePricingDataResponse>>;

public sealed record ValidatePricingDataResponse(
    string OriginalFileName,
    string SourceFileType,
    ExtractionSummaryDto Summary,
    IReadOnlyCollection<ExtractedPricingRowDto> Rows,
    IReadOnlyCollection<ExtractionIssueDto> Issues
);
