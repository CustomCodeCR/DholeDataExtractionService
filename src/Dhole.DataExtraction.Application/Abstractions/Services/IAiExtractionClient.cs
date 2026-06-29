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
