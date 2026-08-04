using Dhole.DataExtraction.Contracts.Extraction;
using Dhole.DataExtraction.Application.Abstractions.Services;

namespace Dhole.DataExtraction.Application.Abstractions.Extraction;

/// <summary>
/// Runs the complete pricing extraction strategy for every entry point.
/// The deterministic pipeline remains the source of validation and persistence;
/// AI is an additional parser whose output must pass through that same pipeline.
/// </summary>
public interface IAutomatedPricingExtractionService
{
    Task<ExtractPricingDataResponse> ExtractDeterministicAsync(
        ExtractionDataRequest request,
        CancellationToken cancellationToken = default
    );

    Task<PreparedAiPricingEmailRequest> PrepareAiRequestAsync(
        ExtractionDataRequest request,
        ExtractPricingDataResponse deterministicResponse,
        AutomatedPricingExtractionContext context,
        string? imageStoragePath = null,
        CancellationToken cancellationToken = default
    );

    Task<AutomatedPricingExtractionResult> ApplyAiResultAsync(
        Guid pricingImportId,
        string correlationId,
        string sourceType,
        Guid? sourceOriginId,
        Guid emailMessageId,
        Guid? emailAttachmentId,
        AiPricingEmailAnalysisResult analysis,
        AutomatedPricingExtractionContext? context = null,
        CancellationToken cancellationToken = default
    );

    Task<AutomatedPricingExtractionResult> ExtractAsync(
        ExtractionDataRequest request,
        AutomatedPricingExtractionContext? context = null,
        CancellationToken cancellationToken = default
    );
}

public sealed record AutomatedPricingExtractionContext(
    Guid? EmailMessageId = null,
    Guid? EmailAttachmentId = null,
    string? FromAddress = null,
    string? Subject = null,
    string? BodyText = null,
    string? BodyHtml = null,
    string? SourceType = null,
    bool ForceAiAnalysis = false
);

public sealed record PreparedAiPricingEmailRequest(
    AiPricingEmailAnalysisRequest Payload,
    string RequestHash,
    string? ImageStoragePath,
    string? ImageContentType
);

public sealed record AutomatedPricingExtractionResult(
    ExtractPricingDataResponse Response,
    bool AiAttempted,
    bool AiApplied,
    Guid? AiExecutionId,
    decimal? AiConfidence,
    string? AiErrorMessage
);
