using Dhole.DataExtraction.Application.Abstractions.Services;

namespace Dhole.DataExtraction.Infrastructure.GrpcClients;

public sealed class AiExtractionGrpcClient : IAiExtractionClient
{
    public Task<AiColumnMappingResult> SuggestColumnMappingsAsync(
        IReadOnlyCollection<string> headers,
        string? rawText,
        string? profileCode = null,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(
            new AiColumnMappingResult(true, Array.Empty<AiColumnMappingItem>())
        );
    }

    public Task<AiTextNormalizationResult> NormalizePricingTextAsync(
        string rawText,
        string? profileCode = null,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(new AiTextNormalizationResult(true, rawText));
    }
}
