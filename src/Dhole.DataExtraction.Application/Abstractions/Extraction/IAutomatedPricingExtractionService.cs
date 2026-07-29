using Dhole.DataExtraction.Contracts.Extraction;

namespace Dhole.DataExtraction.Application.Abstractions.Extraction;

/// <summary>
/// Runs the complete pricing extraction strategy for every entry point.
/// The deterministic pipeline remains the source of validation and persistence;
/// AI is an additional parser whose output must pass through that same pipeline.
/// </summary>
public interface IAutomatedPricingExtractionService
{
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

public sealed record AutomatedPricingExtractionResult(
    ExtractPricingDataResponse Response,
    bool AiAttempted,
    bool AiApplied,
    Guid? AiExecutionId,
    decimal? AiConfidence,
    string? AiErrorMessage
);
