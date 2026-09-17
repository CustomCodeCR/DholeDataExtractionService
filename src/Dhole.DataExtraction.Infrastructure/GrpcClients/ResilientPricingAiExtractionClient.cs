using System.Globalization;
using System.Text;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Dhole.DataExtraction.Infrastructure.GrpcClients;

/// <summary>
/// Adds a guarded second pass around the pricing-specific Qwen extractor.
/// The current source remains the source of truth; Config values are supplied only as
/// canonicalization hints and the first AI result is fed back as a draft when it is incomplete.
/// </summary>
public sealed class ResilientPricingAiExtractionClient(
    AdaptivePricingAiExtractionClient inner,
    IConfigCatalogClient configCatalogClient,
    ILogger<ResilientPricingAiExtractionClient> logger
) : IAiExtractionClient
{
    private static readonly string[] CatalogGroups =
    [
        "pol",
        "poe",
        "pod",
        "container-types",
        "carriers",
        "agents",
        "currencies",
    ];

    public Task<AiColumnMappingResult> SuggestColumnMappingsAsync(
        IReadOnlyCollection<string> headers,
        string? rawText,
        string? profileCode = null,
        CancellationToken cancellationToken = default
    ) => inner.SuggestColumnMappingsAsync(headers, rawText, profileCode, cancellationToken);

    public Task<AiTextNormalizationResult> NormalizePricingTextAsync(
        string rawText,
        string? profileCode = null,
        CancellationToken cancellationToken = default
    ) => inner.NormalizePricingTextAsync(rawText, profileCode, cancellationToken);

    public async Task<AiPricingEmailAnalysisResult> AnalyzePricingEmailAsync(
        AiPricingEmailAnalysisRequest request,
        CancellationToken cancellationToken = default
    )
    {
        var enrichedRequest = await EnrichCatalogHintsAsync(request, cancellationToken);
        var first = await inner.AnalyzePricingEmailAsync(enrichedRequest, cancellationToken);
        var firstIssues = BuildCriticalIssues(first);

        if (first.Success && first.Rows.Count > 0 && firstIssues.Count == 0)
        {
            return first;
        }

        if (!ShouldRetry(first, firstIssues))
        {
            return first;
        }

        var previousRows = first.Rows.Count > 0
            ? first.Rows.Take(40).ToArray()
            : enrichedRequest.PreviousRows.Take(40).ToArray();
        var previousIssues = enrichedRequest.PreviousIssues
            .Concat(firstIssues)
            .GroupBy(issue => $"{issue.Code}|{issue.ColumnName}|{issue.RawValue}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(50)
            .ToArray();

        var retryRequest = enrichedRequest with
        {
            PreviousErrorCode = first.ErrorCode ?? "AI.IncompletePricingRows",
            PreviousErrorMessage = first.ErrorMessage
                ?? "La primera extracción dejó campos críticos incompletos; corrige el borrador contra la fuente original.",
            PreviousConfidence = first.Confidence,
            PreviousRows = previousRows,
            PreviousIssues = previousIssues,
        };
        retryRequest = await EnrichCatalogHintsAsync(retryRequest, cancellationToken);

        logger.LogWarning(
            "Se reintentará extracción Pricing con Qwen. Primer resultado: {ErrorCode}; filas: {Rows}; problemas críticos: {CriticalIssues}.",
            first.ErrorCode ?? (first.Success ? "AI.IncompletePricingRows" : "AI.ExecutionFailed"),
            first.Rows.Count,
            firstIssues.Count
        );

        var second = await inner.AnalyzePricingEmailAsync(retryRequest, cancellationToken);
        return SelectBest(first, second);
    }

    private async Task<AiPricingEmailAnalysisRequest> EnrichCatalogHintsAsync(
        AiPricingEmailAnalysisRequest request,
        CancellationToken cancellationToken
    )
    {
        var sourceProbe = string.Join(
            '\n',
            new[]
            {
                request.Subject,
                request.BodyText,
                request.BodyHtml,
                request.SourceName,
                request.SourceContent,
            }.Where(value => !string.IsNullOrWhiteSpace(value))
        );
        var normalizedSource = Normalize(sourceProbe);
        if (normalizedSource.Length == 0)
        {
            return request;
        }

        var existing = request.CatalogHints
            .ToDictionary(hint => hint.GroupSlug, StringComparer.OrdinalIgnoreCase);

        foreach (var group in CatalogGroups)
        {
            IReadOnlyCollection<ConfigCatalogItemResult> items;
            try
            {
                items = await configCatalogClient.GetActiveCatalogItemsByGroupAsync(
                    group,
                    cancellationToken
                );
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogDebug(
                    exception,
                    "No se pudieron cargar hints de Config para {CatalogGroup}.",
                    group
                );
                continue;
            }

            var detected = items
                .Where(item => item.IsActive && MatchesSource(item, normalizedSource))
                .Take(GetHintLimit(group))
                .Select(item => new AiCatalogItemHint(
                    item.Code,
                    item.Slug,
                    item.Name,
                    item.Value
                ));

            var merged = existing.TryGetValue(group, out var current)
                ? current.Items.Concat(detected)
                : detected;
            var distinct = merged
                .GroupBy(
                    item => $"{item.Code}|{item.Slug}|{item.Name}",
                    StringComparer.OrdinalIgnoreCase
                )
                .Select(grouping => grouping.First())
                .Take(GetHintLimit(group))
                .ToArray();

            if (distinct.Length > 0)
            {
                existing[group] = new AiCatalogGroupHint(group, distinct);
            }
        }

        return request with { CatalogHints = existing.Values.ToArray() };
    }

    private static bool MatchesSource(ConfigCatalogItemResult item, string normalizedSource)
    {
        foreach (var value in new[] { item.Code, item.Slug, item.Name, item.Value })
        {
            var normalized = Normalize(value);
            if (normalized.Length >= 3 && normalizedSource.Contains(normalized, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int GetHintLimit(string group) => group switch
    {
        "pol" or "poe" or "pod" => 20,
        "carriers" or "agents" => 12,
        _ => 10,
    };

    private static IReadOnlyCollection<AiPreviousExtractionIssue> BuildCriticalIssues(
        AiPricingEmailAnalysisResult result
    )
    {
        var issues = new List<AiPreviousExtractionIssue>();

        if (!result.Success || result.Rows.Count == 0)
        {
            issues.Add(new AiPreviousExtractionIssue(
                result.ErrorCode ?? "AI.NoPricingRows",
                result.ErrorMessage ?? "La primera extracción no produjo filas utilizables.",
                true,
                null,
                null
            ));
            return issues;
        }

        foreach (var row in result.Rows.Take(40))
        {
            AddIfMissing(issues, row.OriginPort, "missing_origin_port", "POL", row.OriginPort);
            AddIfMissing(issues, row.PortOfExit, "missing_port_of_exit", "POE", row.PortOfExit);
            AddIfMissing(issues, row.ContainerType, "missing_container_type", "ContainerType", row.ContainerType);

            if (!string.Equals(row.ContainerType?.Trim(), "LCL", StringComparison.OrdinalIgnoreCase))
            {
                AddIfMissing(issues, row.Carrier, "missing_carrier", "Carrier", row.Carrier);
            }

            if (!row.ValidFrom.HasValue)
            {
                issues.Add(new AiPreviousExtractionIssue(
                    "missing_valid_from",
                    "Falta vigencia inicial; búscala de nuevo en encabezados, notas, asunto, cuerpo y tabla de la fuente.",
                    true,
                    "ValidFrom",
                    null
                ));
            }

            if (!row.ValidTo.HasValue)
            {
                issues.Add(new AiPreviousExtractionIssue(
                    "missing_valid_to",
                    "Falta vigencia final; búscala de nuevo en encabezados, notas, asunto, cuerpo y tabla de la fuente.",
                    true,
                    "ValidTo",
                    null
                ));
            }

            if (!row.OceanFreight.HasValue && !row.TotalCost.HasValue && !row.TotalSale.HasValue)
            {
                issues.Add(new AiPreviousExtractionIssue(
                    "missing_rate_amount",
                    "Falta el monto de tarifa. Revisa Ocean Freight, Freight, Rate, All In y columnas por equipo sin confundir cargos locales con flete marítimo.",
                    true,
                    "OceanFreight",
                    null
                ));
            }
        }

        return issues
            .GroupBy(issue => $"{issue.Code}|{issue.ColumnName}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static void AddIfMissing(
        ICollection<AiPreviousExtractionIssue> issues,
        string? value,
        string code,
        string column,
        string? rawValue
    )
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        issues.Add(new AiPreviousExtractionIssue(
            code,
            $"Falta {column}; vuelve a buscarlo en la fuente actual y usa catalogHints solo para normalizar una etiqueta que realmente aparezca en la fuente.",
            true,
            column,
            rawValue
        ));
    }

    private static bool ShouldRetry(
        AiPricingEmailAnalysisResult first,
        IReadOnlyCollection<AiPreviousExtractionIssue> criticalIssues
    )
    {
        if (first.Success)
        {
            return criticalIssues.Count > 0;
        }

        var code = first.ErrorCode?.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            return true;
        }

        return code.Equals("AI.InvalidStructuredOutput", StringComparison.OrdinalIgnoreCase)
            || code.Equals("AI.InvalidPricingResponse", StringComparison.OrdinalIgnoreCase)
            || code.Equals("AI.NoPricingRows", StringComparison.OrdinalIgnoreCase)
            || code.Equals("AI.ExecutionFailed", StringComparison.OrdinalIgnoreCase);
    }

    private static AiPricingEmailAnalysisResult SelectBest(
        AiPricingEmailAnalysisResult first,
        AiPricingEmailAnalysisResult second
    )
    {
        if (second.Success && !first.Success)
        {
            return second;
        }

        if (first.Success && !second.Success)
        {
            return first;
        }

        if (!first.Success && !second.Success)
        {
            return second;
        }

        var firstIssues = BuildCriticalIssues(first).Count;
        var secondIssues = BuildCriticalIssues(second).Count;
        if (secondIssues != firstIssues)
        {
            return secondIssues < firstIssues ? second : first;
        }

        if (second.Rows.Count != first.Rows.Count)
        {
            return second.Rows.Count > first.Rows.Count ? second : first;
        }

        return second.Confidence >= first.Confidence ? second : first;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }
}
