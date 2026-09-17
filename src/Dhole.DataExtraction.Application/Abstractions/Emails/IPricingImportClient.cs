namespace Dhole.DataExtraction.Application.Abstractions.Emails;

public interface IPricingImportClient
{
    Task<PricingImportSubmissionResult> SubmitAsync(
        PricingImportSubmissionRequest request,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyCollection<PricingLearningExample>> GetLearningContextAsync(
        int limit = 12,
        CancellationToken cancellationToken = default
    );
}

public sealed record PricingLearningExample(
    string Outcome,
    string? Pol,
    string? Poe,
    string? Pod,
    string? ContainerType,
    string? Carrier,
    string? Currency,
    decimal? OceanFreight,
    decimal? OriginCharges,
    decimal? DestinationCharges,
    decimal? Surcharges,
    decimal? TotalCost,
    int FreeDays,
    int? TransitDays,
    DateTime ValidFrom,
    DateTime ValidTo,
    string? Commodity,
    string? SpaceComment,
    string? FeedbackOutcome = null,
    IReadOnlyCollection<string>? FeedbackReasonCodes = null,
    string? FeedbackComment = null,
    string? CorrectValue = null,
    string? OriginalSnapshotJson = null,
    string? ReviewedSnapshotJson = null
);

public sealed record PricingImportSubmissionRequest(
    Guid PricingImportId,
    Guid ExtractionExecutionId,
    Guid? EmailMessageId,
    Guid? EmailAttachmentId,
    string SourceType,
    string? FromAddress,
    string? Subject,
    string? OriginalFileName,
    Contracts.Extraction.ExtractPricingDataResponse Response
);

public sealed record PricingImportSubmissionResult(
    bool Success,
    Guid? PricingImportBatchId,
    string? ErrorMessage
);
