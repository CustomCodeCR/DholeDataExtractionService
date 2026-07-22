namespace Dhole.DataExtraction.Application.Abstractions.Services;

public interface IAiExtractionClient
{
    Task<AiColumnMappingResult> SuggestColumnMappingsAsync(
        IReadOnlyCollection<string> headers,
        string? rawText,
        string? profileCode = null,
        CancellationToken cancellationToken = default
    );

    Task<AiTextNormalizationResult> NormalizePricingTextAsync(
        string rawText,
        string? profileCode = null,
        CancellationToken cancellationToken = default
    );

    Task<AiPricingEmailAnalysisResult> AnalyzePricingEmailAsync(
        AiPricingEmailAnalysisRequest request,
        CancellationToken cancellationToken = default
    );
}

public sealed record AiColumnMappingResult(
    bool Success,
    IReadOnlyCollection<AiColumnMappingItem> Mappings,
    string? ErrorMessage = null
);

public sealed record AiColumnMappingItem(string SourceColumn, string TargetField, decimal Score);

public sealed record AiTextNormalizationResult(
    bool Success,
    string? NormalizedText,
    string? ErrorMessage = null
);

public sealed record AiPricingEmailAnalysisRequest(
    Guid EmailMessageId,
    Guid? EmailAttachmentId,
    string FromAddress,
    string Subject,
    string? BodyText,
    string? BodyHtml,
    string SourceType,
    string SourceName,
    string? SourceContentType,
    string SourceContent,
    string CorrelationId,
    string? PreviousErrorCode,
    string? PreviousErrorMessage,
    decimal PreviousConfidence
);

public sealed record AiPricingEmailAnalysisResult(
    bool Success,
    Guid? AiExecutionId,
    decimal Confidence,
    IReadOnlyCollection<AiPricingEmailRow> Rows,
    IReadOnlyCollection<string> Warnings,
    string? ErrorCode = null,
    string? ErrorMessage = null
);

public sealed record AiPricingEmailRow(
    string? OriginPort,
    string? PortOfExit,
    string? DestinationPort,
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
