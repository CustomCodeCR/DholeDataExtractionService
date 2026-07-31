using Dhole.DataExtraction.Contracts.Extraction;

namespace Dhole.DataExtraction.Contracts.AsyncEmail;

public static class AsyncEmailMessageTypes
{
    public const string AiRequested = "ai.pricing-email-analysis.requested";
    public const string AiStarted = "ai.pricing-email-analysis.started";
    public const string AiCompleted = "ai.pricing-email-analysis.completed";
    public const string AiFailed = "ai.pricing-email-analysis.failed";
    public const string PricingRequested = "pricing.import-from-extraction.requested";
    public const string PricingCompleted = "pricing.import-from-extraction.completed";
    public const string PricingFailed = "pricing.import-from-extraction.failed";
}

public sealed record AiPricingEmailAnalysisRequestedIntegrationEvent(
    Guid EventId,
    Guid RequestId,
    Guid EmailExtractionJobId,
    Guid EmailMessageId,
    Guid? EmailAttachmentId,
    Guid ProvisionalPricingImportId,
    Guid? ExtractionExecutionId,
    string CorrelationId,
    string RequestHash,
    string PayloadUrl,
    DateTime OccurredAtUtc
);

public sealed record AiPricingEmailAnalysisStartedIntegrationEvent(
    Guid EventId,
    Guid RequestId,
    Guid EmailExtractionJobId,
    Guid AiJobId,
    string CorrelationId,
    DateTime OccurredAtUtc
);

public sealed record AiPricingEmailAnalysisCompletedIntegrationEvent(
    Guid EventId,
    Guid RequestId,
    Guid EmailExtractionJobId,
    Guid AiJobId,
    Guid AiExecutionId,
    string CorrelationId,
    string RequestHash,
    decimal Confidence,
    IReadOnlyCollection<AiPricingEmailResultRow> Rows,
    IReadOnlyCollection<string> Warnings,
    DateTime OccurredAtUtc
);

public sealed record AiPricingEmailResultRow(
    string? Pol,
    string? Poe,
    string? Pod,
    string? ContainerType,
    string? Carrier,
    string? Agent,
    string? Commodity,
    string? Currency,
    int? FreeDays,
    int? TransitDays,
    DateTime? ValidFrom,
    DateTime? ValidTo,
    decimal? OceanFreight,
    decimal? OriginCharges,
    decimal? DestinationCharges,
    decimal? Surcharges,
    decimal? TotalCost,
    decimal? TotalSale,
    decimal? Profit,
    decimal? Margin,
    string? SpaceComment,
    string? Remarks
);

public sealed record AiPricingEmailAnalysisFailedIntegrationEvent(
    Guid EventId,
    Guid RequestId,
    Guid EmailExtractionJobId,
    Guid AiJobId,
    Guid? AiExecutionId,
    string CorrelationId,
    string RequestHash,
    string ErrorCode,
    string ErrorMessage,
    bool IsTransient,
    int AttemptCount,
    DateTime OccurredAtUtc
);

public sealed record PricingImportFromExtractionRequestedIntegrationEvent(
    Guid EventId,
    Guid RequestId,
    Guid EmailExtractionJobId,
    Guid ExtractionExecutionId,
    Guid PricingImportId,
    Guid EmailMessageId,
    Guid? EmailAttachmentId,
    string SourceType,
    string FromAddress,
    string Subject,
    string OriginalFileName,
    decimal ConfidenceScore,
    string ContentSourceType,
    string CorrelationId,
    ExtractPricingDataResponse Response,
    DateTime OccurredAtUtc
);

public sealed record PricingImportFromExtractionCompletedIntegrationEvent(
    Guid EventId,
    Guid RequestId,
    Guid EmailExtractionJobId,
    Guid ExtractionExecutionId,
    Guid PricingImportBatchId,
    int PersistedRows,
    int SkippedRows,
    string CorrelationId,
    DateTime OccurredAtUtc
);

public sealed record PricingImportFromExtractionFailedIntegrationEvent(
    Guid EventId,
    Guid RequestId,
    Guid EmailExtractionJobId,
    Guid ExtractionExecutionId,
    Guid PricingImportId,
    string ErrorCode,
    string ErrorMessage,
    bool IsTransient,
    int AttemptCount,
    string CorrelationId,
    DateTime OccurredAtUtc
);
