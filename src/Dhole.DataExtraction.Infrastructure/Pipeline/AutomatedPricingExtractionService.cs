using System.Globalization;
using System.Text;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Application.Extraction;
using Dhole.DataExtraction.Contracts.Extraction;
using Dhole.DataExtraction.Infrastructure.Files;
using Dhole.DataExtraction.Infrastructure.Mapping;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dhole.DataExtraction.Infrastructure.Pipeline;

/// <summary>
/// Shared extraction strategy for manual uploads, email bodies and attachments.
/// AI never bypasses the deterministic pipeline: its structured rows are converted
/// to a neutral CSV and validated against Config by <see cref="IExtractionPipeline"/>.
/// </summary>
public sealed class AutomatedPricingExtractionService(
    IExtractionPipeline pipeline,
    IAiExtractionClient aiExtractionClient,
    IAiEmailContentReader contentReader,
    IConfigCatalogClient configCatalogClient,
    IConfiguration configuration,
    ILogger<AutomatedPricingExtractionService> logger
) : IAutomatedPricingExtractionService
{
    public async Task<AutomatedPricingExtractionResult> ExtractAsync(
        ExtractionDataRequest request,
        AutomatedPricingExtractionContext? context = null,
        CancellationToken cancellationToken = default
    )
    {
        var deterministicResponse = await pipeline.ExtractPricingDataAsync(
            request,
            cancellationToken
        );

        if (IsAiGeneratedRequest(request))
        {
            return WithoutAi(deterministicResponse);
        }

        var requireAiResult = ReadBoolean(
            configuration["AI:AutomaticExtraction:RequireAiResult"],
            true
        );

        if (!IsAiEnabled())
        {
            const string error = "La etapa obligatoria de formateo con IA está deshabilitada.";
            return requireAiResult
                ? RequiredAiFailure(deterministicResponse, error, aiAttempted: false)
                : WithoutAi(deterministicResponse);
        }

        if (
            !requireAiResult
            && context?.ForceAiAnalysis != true
            && !ReadBoolean(
                configuration["AI:AutomaticExtraction:AnalyzeEverySource"],
                true
            )
            && !NeedsAiAnalysis(deterministicResponse)
        )
        {
            return WithoutAi(deterministicResponse);
        }

        try
        {
            var sourceContent = await contentReader.ReadAsTextAsync(
                request.OriginalFileName,
                request.ContentType,
                request.FileExtension,
                request.FileContent,
                cancellationToken
            );

            var catalogHints = await BuildCatalogHintsAsync(
                deterministicResponse,
                sourceContent,
                cancellationToken
            );
            var sourceType = FirstNotEmpty(
                request.SourceOriginType,
                context?.SourceType,
                "ManualUpload"
            )!;
            var analysis = await aiExtractionClient.AnalyzePricingEmailAsync(
                new AiPricingEmailAnalysisRequest(
                    context?.EmailMessageId
                        ?? request.SourceEmailMessageId
                        ?? request.PricingImportId,
                    context?.EmailAttachmentId ?? request.SourceEmailAttachmentId,
                    context?.FromAddress ?? string.Empty,
                    FirstNotEmpty(
                        context?.Subject,
                        $"Extracción de tarifa: {request.OriginalFileName}"
                    )!,
                    context?.BodyText,
                    context?.BodyHtml,
                    sourceType,
                    request.OriginalFileName,
                    request.ContentType,
                    sourceContent,
                    request.CorrelationId,
                    deterministicResponse.ErrorCode,
                    deterministicResponse.ErrorMessage,
                    CalculatePreviousConfidence(deterministicResponse),
                    BuildPreviousRows(deterministicResponse),
                    BuildPreviousIssues(deterministicResponse),
                    catalogHints,
                    IsImage(request) ? Convert.ToBase64String(request.FileContent) : null,
                    IsImage(request) ? ResolveImageMimeType(request) : null
                ),
                cancellationToken
            );

            if (!analysis.Success || analysis.Rows.Count == 0)
            {
                var error = analysis.ErrorMessage
                    ?? analysis.Warnings.FirstOrDefault()
                    ?? "AI no devolvió filas de tarifas utilizables.";

                return requireAiResult
                    ? RequiredAiFailure(
                        deterministicResponse,
                        error,
                        analysis.AiExecutionId,
                        analysis.Confidence
                    )
                    : new AutomatedPricingExtractionResult(
                        WithAiError(deterministicResponse, error),
                        true,
                        false,
                        analysis.AiExecutionId,
                        analysis.Confidence,
                        error
                    );
            }

            var csvContent = BuildNormalizedCsv(analysis.Rows, analysis.Warnings);
            var csvBytes = Encoding.UTF8.GetBytes(csvContent);
            var normalizedRequest = new ExtractionDataRequest(
                request.PricingImportId,
                request.CorrelationId,
                $"ai-automatic-{request.PricingImportId:N}.csv",
                "text/csv",
                ".csv",
                csvBytes.LongLength,
                FileHashCalculator.ComputeSha256(csvBytes),
                request.ProfileCode,
                request.RequestedBy,
                FirstNotEmpty(request.RequestedByName, "AI automatic extraction"),
                csvBytes
            )
            {
                SourceOriginType = BuildAiSourceOrigin(sourceType),
                SourceOriginId = request.SourceOriginId,
                SourceEmailMessageId = context?.EmailMessageId
                    ?? request.SourceEmailMessageId,
                SourceEmailAttachmentId = context?.EmailAttachmentId
                    ?? request.SourceEmailAttachmentId,
            };

            var aiValidatedResponse = await pipeline.ExtractPricingDataAsync(
                normalizedRequest,
                cancellationToken
            );

            if (!IsUsable(aiValidatedResponse))
            {
                var error = aiValidatedResponse.ErrorMessage
                    ?? "DataExtraction no pudo validar la salida estructurada de AI.";

                return requireAiResult
                    ? RequiredAiFailure(
                        deterministicResponse,
                        error,
                        analysis.AiExecutionId,
                        analysis.Confidence
                    )
                    : new AutomatedPricingExtractionResult(
                        WithAiError(deterministicResponse, error),
                        true,
                        false,
                        analysis.AiExecutionId,
                        analysis.Confidence,
                        error
                    );
            }

            var useAiResponse = ReadBoolean(
                configuration["AI:AutomaticExtraction:PreferAiResult"],
                true
            )
                ? IsUsable(aiValidatedResponse)
                : ShouldSelectAiResponse(deterministicResponse, aiValidatedResponse);
            var selectedResponse = useAiResponse
                ? aiValidatedResponse
                : deterministicResponse;

            logger.LogInformation(
                "Extracción automática completada para {SourceName}. AI intentada: true; "
                    + "AI seleccionada: {AiApplied}; filas determinísticas: {DeterministicRows}; "
                    + "filas AI validadas: {AiRows}; ejecución AI: {AiExecutionId}.",
                request.OriginalFileName,
                useAiResponse,
                deterministicResponse.Rows.Count,
                aiValidatedResponse.Rows.Count,
                analysis.AiExecutionId
            );

            return new AutomatedPricingExtractionResult(
                selectedResponse,
                true,
                useAiResponse,
                analysis.AiExecutionId,
                analysis.Confidence,
                null
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "AI no pudo completar la extracción de {SourceName}; se aplicará la política "
                    + "AI:AutomaticExtraction:RequireAiResult.",
                request.OriginalFileName
            );

            return requireAiResult
                ? RequiredAiFailure(deterministicResponse, exception.Message)
                : new AutomatedPricingExtractionResult(
                    WithAiError(deterministicResponse, exception.Message),
                    true,
                    false,
                    null,
                    null,
                    exception.Message
                );
        }
    }

    private async Task<IReadOnlyCollection<AiCatalogGroupHint>> BuildCatalogHintsAsync(
        ExtractPricingDataResponse response,
        string sourceContent,
        CancellationToken cancellationToken
    )
    {
        var hints = new List<AiCatalogGroupHint>();
        var normalizedSource = NormalizeSearchText(sourceContent);

        foreach (var groupSlug in PricingCatalogSlugs.RowCatalogs)
        {
            IReadOnlyCollection<ConfigCatalogItemResult> items;
            try
            {
                items = await configCatalogClient.GetActiveCatalogItemsByGroupAsync(
                    groupSlug,
                    cancellationToken
                );
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(
                    exception,
                    "No se pudieron cargar sugerencias del catálogo {CatalogGroup} para AI.",
                    groupSlug
                );
                continue;
            }

            var searchTerms = GetPreviousValues(response, groupSlug)
                .Select(NormalizeSearchText)
                .Where(value => value.Length >= 2)
                .ToArray();
            var selected = items
                .Where(item => item.IsActive)
                .Select(item => new
                {
                    Item = item,
                    Score = ScoreCatalogItem(item, searchTerms, normalizedSource),
                })
                .OrderByDescending(result => result.Score)
                .ThenBy(result => result.Item.Name)
                .Take(GetCatalogHintLimit(groupSlug))
                .Select(result => new AiCatalogItemHint(
                    result.Item.Code,
                    result.Item.Slug,
                    result.Item.Name,
                    result.Item.Value
                ))
                .ToArray();

            if (selected.Length > 0)
            {
                hints.Add(new AiCatalogGroupHint(groupSlug, selected));
            }
        }

        return hints;
    }

    private static int GetCatalogHintLimit(string groupSlug)
    {
        return groupSlug switch
        {
            PricingCatalogSlugs.Pol or PricingCatalogSlugs.Poe or PricingCatalogSlugs.Pod => 300,
            PricingCatalogSlugs.Agents => 200,
            _ => 120,
        };
    }

    private static IReadOnlyCollection<AiPricingEmailRow> BuildPreviousRows(
        ExtractPricingDataResponse response
    )
    {
        return response.Rows
            .Take(200)
            .Select(row => new AiPricingEmailRow(
                row.OriginPort,
                row.PortOfExit,
                row.DestinationPort,
                row.ContainerType,
                row.Carrier,
                row.Agent,
                row.Commodity,
                row.Currency,
                row.FreeDays,
                row.TransitDays,
                row.ValidFrom,
                row.ValidTo,
                row.OceanFreight,
                row.OriginCharges,
                row.DestinationCharges,
                row.Surcharges,
                row.TotalCost,
                row.TotalSale,
                row.Profit,
                row.Margin,
                row.SpaceComment,
                row.Remarks
            ))
            .ToArray();
    }

    private static IReadOnlyCollection<AiPreviousExtractionIssue> BuildPreviousIssues(
        ExtractPricingDataResponse response
    )
    {
        return response.Issues
            .Take(300)
            .Select(issue => new AiPreviousExtractionIssue(
                issue.Code,
                issue.Message,
                issue.IsBlocking,
                issue.ColumnName,
                issue.RawValue
            ))
            .ToArray();
    }

    private static IEnumerable<string> GetPreviousValues(
        ExtractPricingDataResponse response,
        string groupSlug
    )
    {
        foreach (var row in response.Rows)
        {
            var value = groupSlug switch
            {
                PricingCatalogSlugs.Pol => row.OriginPort,
                PricingCatalogSlugs.Poe => row.PortOfExit,
                PricingCatalogSlugs.Pod => row.DestinationPort,
                PricingCatalogSlugs.ContainerTypes => row.ContainerType,
                PricingCatalogSlugs.Carriers => row.Carrier,
                PricingCatalogSlugs.Agents => row.Agent,
                PricingCatalogSlugs.Currencies => row.Currency,
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }
        }
    }

    private static int ScoreCatalogItem(
        ConfigCatalogItemResult item,
        IReadOnlyCollection<string> searchTerms,
        string normalizedSource
    )
    {
        var values = new[] { item.Code, item.Slug, item.Name, item.Value }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(NormalizeSearchText)
            .Where(value => value.Length >= 2)
            .ToArray();
        var score = 0;

        foreach (var candidate in values)
        {
            if (searchTerms.Any(term =>
                candidate.Equals(term, StringComparison.OrdinalIgnoreCase)
                || candidate.Contains(term, StringComparison.OrdinalIgnoreCase)
                || term.Contains(candidate, StringComparison.OrdinalIgnoreCase)
            ))
            {
                score = Math.Max(score, 100);
            }
            else if (normalizedSource.Contains(candidate, StringComparison.OrdinalIgnoreCase))
            {
                score = Math.Max(score, 50);
            }
        }

        return score;
    }

    private static string NormalizeSearchText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in TextContentDecoder.Clean(value)
            .Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character)
                ? char.ToUpperInvariant(character)
                : ' ');
        }

        return string.Concat(builder.ToString().Split(
            ' ',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        ));
    }

    private static string ResolveImageMimeType(ExtractionDataRequest request)
    {
        if (request.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
        {
            return request.ContentType;
        }

        return request.FileExtension?.Trim().ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            _ => "image/png",
        };
    }

    private static bool IsImage(ExtractionDataRequest request)
    {
        var extension = request.FileExtension?.Trim().ToLowerInvariant();
        return request.ContentType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true
            || extension is ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".tif" or ".tiff";
    }

    private bool IsAiEnabled()
    {
        var automaticValue = configuration["AI:AutomaticExtraction:Enabled"];
        return bool.TryParse(automaticValue, out var automaticEnabled)
            ? automaticEnabled
            : ReadBoolean(configuration["AI:EmailFallback:Enabled"], true);
    }

    private static bool NeedsAiAnalysis(ExtractPricingDataResponse response)
    {
        return !IsUsable(response)
            || response.Issues.Any(issue =>
                issue.IsBlocking
                && !issue.Code.StartsWith("unknown_", StringComparison.OrdinalIgnoreCase)
            );
    }

    private static bool IsUsable(ExtractPricingDataResponse response)
    {
        return response.Success
            && response.Rows.Count > 0
            && response.Summary.TotalRows > 0;
    }

    private static decimal CalculateQualityScore(ExtractPricingDataResponse response)
    {
        if (!IsUsable(response))
        {
            return decimal.MinValue;
        }

        var score = response.Rows.Count * 1_000m;
        score += response.Summary.ValidRows * 250m;
        score += response.Summary.WarningRows * 100m;
        score -= response.Summary.InvalidRows * 500m;
        score -= response.Issues.Count(issue => issue.IsBlocking) * 300m;

        foreach (var row in response.Rows)
        {
            score += HasValue(row.OriginPort) ? 40m : 0m;
            score += HasValue(row.PortOfExit) ? 40m : 0m;
            score += HasValue(row.ContainerType) ? 40m : 0m;
            score += HasValue(row.Carrier) ? 40m : 0m;
            score += HasValue(row.Currency) ? 25m : 0m;
            score += row.ValidFrom.HasValue ? 25m : 0m;
            score += row.ValidTo.HasValue ? 25m : 0m;
            score += row.OceanFreight.HasValue || row.TotalSale.HasValue ? 40m : 0m;
            score += row.OriginPortReference is not null ? 5m : 0m;
            score += row.PortOfExitReference is not null ? 5m : 0m;
            score += row.ContainerTypeReference is not null ? 5m : 0m;
            score += row.CarrierReference is not null ? 5m : 0m;
            score += row.CurrencyReference is not null ? 5m : 0m;
        }

        return score;
    }

    private static bool ShouldSelectAiResponse(
        ExtractPricingDataResponse deterministicResponse,
        ExtractPricingDataResponse aiResponse
    )
    {
        if (!IsUsable(aiResponse))
        {
            return false;
        }

        if (!IsUsable(deterministicResponse))
        {
            return true;
        }

        var deterministicBlockingRatio = CalculateBlockingRowRatio(
            deterministicResponse
        );
        var aiBlockingRatio = CalculateBlockingRowRatio(aiResponse);

        if (aiBlockingRatio != deterministicBlockingRatio)
        {
            return aiBlockingRatio < deterministicBlockingRatio;
        }

        return CalculateQualityScore(aiResponse)
            > CalculateQualityScore(deterministicResponse);
    }

    private static decimal CalculateBlockingRowRatio(
        ExtractPricingDataResponse response
    )
    {
        if (response.Rows.Count == 0)
        {
            return 1m;
        }

        var blockingRows = response.Issues
            .Where(issue => issue.IsBlocking)
            .Select(issue => issue.ExtractedPricingRowId)
            .Where(rowId => rowId.HasValue)
            .Distinct()
            .Count();
        var hasGlobalBlockingIssue = response.Issues.Any(issue =>
            issue.IsBlocking && !issue.ExtractedPricingRowId.HasValue
        );

        return hasGlobalBlockingIssue
            ? 1m
            : decimal.Divide(blockingRows, response.Rows.Count);
    }

    private static decimal CalculatePreviousConfidence(
        ExtractPricingDataResponse response
    )
    {
        if (!IsUsable(response) || response.Summary.TotalRows <= 0)
        {
            return 0m;
        }

        var usableRows = response.Summary.ValidRows + response.Summary.WarningRows;
        return Math.Clamp(
            decimal.Divide(usableRows, response.Summary.TotalRows) * 100m,
            0m,
            100m
        );
    }

    internal static string BuildNormalizedCsv(
        IReadOnlyCollection<AiPricingEmailRow> rows,
        IReadOnlyCollection<string> warnings
    )
    {
        string[] headers =
        [
            "POL",
            "POE",
            "POD",
            "Equipo",
            "Naviera",
            "Agente",
            "Commodity",
            "Moneda",
            "Dias Libres",
            "Dias Transito",
            "Valid From",
            "Valid To",
            "Ocean Freight",
            "Origin Charges",
            "Destination Charges",
            "Surcharges",
            "Total Cost",
            "Total Sale",
            "Profit",
            "Margin",
            "Space",
            "Remarks",
        ];

        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", headers.Select(EscapeCsv)));

        var globalWarnings = warnings.Count == 0
            ? null
            : $"AI warnings: {string.Join(" | ", warnings)}";
        var emittedRows = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var containerVariants = PricingContainerVariants.Expand(row.ContainerType);
            if (containerVariants.Count == 0)
            {
                containerVariants = string.IsNullOrWhiteSpace(row.ContainerType)
                    ? [string.Empty]
                    : [row.ContainerType.Trim()];
            }

            foreach (var containerType in containerVariants)
            {
                var remarks = JoinRemarks(row.Remarks, globalWarnings);

                // POE and POD are independent. Generic destination/Port of Discharge
                // is PortOfExit; only an explicit POD/Place of Delivery is DestinationPort.
                string?[] values =
                [
                    row.OriginPort,
                    row.PortOfExit,
                    row.DestinationPort,
                    containerType,
                    row.Carrier,
                    row.Agent,
                    row.Commodity,
                    row.Currency,
                    Format(row.FreeDays),
                    Format(row.TransitDays),
                    Format(row.ValidFrom),
                    Format(row.ValidTo),
                    Format(row.OceanFreight),
                    Format(row.OriginCharges),
                    Format(row.DestinationCharges),
                    Format(row.Surcharges),
                    Format(row.TotalCost),
                    Format(row.TotalSale),
                    Format(row.Profit),
                    Format(row.Margin),
                    row.SpaceComment,
                    remarks,
                ];

                var serialized = string.Join(",", values.Select(EscapeCsv));
                if (emittedRows.Add(serialized))
                {
                    builder.AppendLine(serialized);
                }
            }
        }

        return builder.ToString();
    }

    private static string BuildAiSourceOrigin(string sourceType)
    {
        var normalized = string.IsNullOrWhiteSpace(sourceType)
            ? "ManualUpload"
            : sourceType.Trim();

        return normalized.EndsWith("AiFallback", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : $"{normalized}AiFallback";
    }

    private static bool IsAiGeneratedRequest(ExtractionDataRequest request)
    {
        return request.OriginalFileName.StartsWith(
                "ai-automatic-",
                StringComparison.OrdinalIgnoreCase
            )
            || (
                request.SourceOriginType?.Contains(
                    "AiFallback",
                    StringComparison.OrdinalIgnoreCase
                ) == true
            );
    }

    private static AutomatedPricingExtractionResult RequiredAiFailure(
        ExtractPricingDataResponse response,
        string error,
        Guid? aiExecutionId = null,
        decimal? aiConfidence = null,
        bool aiAttempted = true
    )
    {
        var failedResponse = response with
        {
            Success = false,
            ErrorCode = "AI.RequiredFormattingFailed",
            ErrorMessage = "La IA no pudo completar el formateo obligatorio antes de enviar los datos a Pricing. "
                + error.Trim(),
        };

        return new AutomatedPricingExtractionResult(
            failedResponse,
            aiAttempted,
            false,
            aiExecutionId,
            aiConfidence,
            error
        );
    }

    private static AutomatedPricingExtractionResult WithoutAi(
        ExtractPricingDataResponse response
    ) => new(response, false, false, null, null, null);

    private static ExtractPricingDataResponse WithAiError(
        ExtractPricingDataResponse response,
        string aiError
    )
    {
        if (IsUsable(response) || string.IsNullOrWhiteSpace(aiError))
        {
            return response;
        }

        var deterministicError = string.IsNullOrWhiteSpace(response.ErrorMessage)
            ? "La extracción determinística no produjo filas utilizables."
            : response.ErrorMessage.Trim();

        return response with
        {
            ErrorMessage = $"{deterministicError} | AI: {aiError.Trim()}"
        };
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return escaped.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{escaped}\""
            : escaped;
    }

    private static string? Format(decimal? value)
    {
        return value?.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private static string? Format(int? value)
    {
        return value?.ToString(CultureInfo.InvariantCulture);
    }

    private static string? Format(DateTime? value)
    {
        return value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string? JoinRemarks(string? rowRemarks, string? globalWarnings)
    {
        if (string.IsNullOrWhiteSpace(rowRemarks))
        {
            return globalWarnings;
        }

        if (string.IsNullOrWhiteSpace(globalWarnings))
        {
            return rowRemarks.Trim();
        }

        return $"{rowRemarks.Trim()} | {globalWarnings}";
    }

    private static string? FirstNotEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private static bool HasValue(string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool ReadBoolean(string? value, bool fallback)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
