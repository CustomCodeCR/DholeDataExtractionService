using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Application.Extraction;
using Dhole.DataExtraction.Contracts.Extraction;
using Dhole.DataExtraction.Infrastructure.Email;
using Dhole.DataExtraction.Infrastructure.Extraction.Email;
using Dhole.DataExtraction.Infrastructure.Files;
using Dhole.DataExtraction.Infrastructure.Mapping;
using Dhole.DataExtraction.Infrastructure.Normalization;
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
    private const int MaximumPreviousRows = 20;
    private const int MaximumPreviousIssues = 30;

    private static readonly JsonSerializerOptions RequestJsonOptions = new(
        JsonSerializerDefaults.Web
    )
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public Task<ExtractPricingDataResponse> ExtractDeterministicAsync(
        ExtractionDataRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (!IsSupportedExtractionSource(request))
        {
            return Task.FromResult(CreateUnsupportedSourceResponse(request));
        }

        return pipeline.ExtractPricingDataAsync(request, cancellationToken);
    }

    public async Task<PreparedAiPricingEmailRequest> PrepareAiRequestAsync(
        ExtractionDataRequest request,
        ExtractPricingDataResponse deterministicResponse,
        AutomatedPricingExtractionContext context,
        string? imageStoragePath = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!IsSupportedExtractionSource(request))
        {
            throw new InvalidOperationException(
                "AI solo puede normalizar cuerpo de correo, PDF, CSV o XLSX."
            );
        }

        var sourceType = FirstNotEmpty(
            request.SourceOriginType,
            context.SourceType,
            "Email"
        )!;
        var sourceContent = await contentReader.ReadAsTextAsync(
            request.OriginalFileName,
            request.ContentType,
            request.FileExtension,
            request.FileContent,
            cancellationToken
        );
        var focusedSourceContent = sourceType.Contains(
            "Body",
            StringComparison.OrdinalIgnoreCase
        )
            ? EmailPricingContentSelector.SelectBestPricingSection(sourceContent)
            : sourceContent;
        var limitedSourceContent = LimitPreservingEdges(
            focusedSourceContent,
            ReadPositiveInt(
                configuration[
                    "AI:EmailFallback:MaximumContentCharacters"
                ],
                12_000
            ),
            "\n[CONTENIDO INTERMEDIO OMITIDO]\n"
        );
        var emailContext = BuildLimitedEmailContext(
            context.BodyText,
            context.BodyHtml
        );
        var normalizedSubject = EmailSubjectNormalizer.NormalizeForExtraction(
            context.Subject
        );
        var catalogHints = await BuildCatalogHintsAsync(
            deterministicResponse,
            BuildCatalogSearchContent(
                normalizedSubject,
                context.BodyText,
                context.BodyHtml,
                limitedSourceContent
            ),
            cancellationToken
        );
        var payload = new AiPricingEmailAnalysisRequest(
            context.EmailMessageId
                ?? request.SourceEmailMessageId
                ?? request.PricingImportId,
            context.EmailAttachmentId ?? request.SourceEmailAttachmentId,
            context.FromAddress ?? string.Empty,
            FirstNotEmpty(
                normalizedSubject,
                $"Extracción de tarifa: {request.OriginalFileName}"
            )!,
            emailContext,
            null,
            sourceType,
            request.OriginalFileName,
            request.ContentType,
            limitedSourceContent,
            request.CorrelationId,
            deterministicResponse.ErrorCode,
            deterministicResponse.ErrorMessage,
            CalculatePreviousConfidence(deterministicResponse),
            BuildPreviousRows(deterministicResponse),
            BuildPreviousIssues(deterministicResponse),
            catalogHints,
            SourceImageBase64: null,
            SourceImageMimeType: null
        );
        var payloadJson = JsonSerializer.Serialize(payload, RequestJsonOptions);
        var requestHash = ComputeSha256(
            string.Concat(payloadJson, "|file-hash:", request.FileHash)
        );

        return new PreparedAiPricingEmailRequest(
            payload,
            requestHash,
            ImageStoragePath: null,
            ImageContentType: null
        );
    }

    public async Task<AutomatedPricingExtractionResult> ApplyAiResultAsync(
        Guid pricingImportId,
        string correlationId,
        string sourceType,
        Guid? sourceOriginId,
        Guid emailMessageId,
        Guid? emailAttachmentId,
        AiPricingEmailAnalysisResult analysis,
        AutomatedPricingExtractionContext? context = null,
        CancellationToken cancellationToken = default
    )
    {
        if (!analysis.Success || analysis.Rows.Count == 0)
        {
            var error = analysis.ErrorMessage
                ?? analysis.Warnings.FirstOrDefault()
                ?? "AI no devolvió filas de tarifas utilizables.";

            return new AutomatedPricingExtractionResult(
                CreateFailedAiResponse(
                    pricingImportId,
                    correlationId,
                    analysis.ErrorCode ?? "AI.NoPricingRows",
                    error
                ),
                true,
                false,
                analysis.AiExecutionId,
                analysis.Confidence,
                error
            );
        }

        var normalizedRows = NormalizeAiRowsForEmailSemantics(analysis.Rows, context);
        var csvContent = BuildNormalizedCsv(normalizedRows, analysis.Warnings);
        var csvBytes = Encoding.UTF8.GetBytes(csvContent);
        var normalizedRequest = new ExtractionDataRequest(
            pricingImportId,
            correlationId,
            $"ai-email-{pricingImportId:N}.csv",
            "text/csv",
            ".csv",
            csvBytes.LongLength,
            FileHashCalculator.ComputeSha256(csvBytes),
            null,
            null,
            "AI asynchronous email extraction",
            csvBytes
        )
        {
            SourceOriginType = BuildAiSourceOrigin(sourceType),
            SourceOriginId = sourceOriginId,
            SourceEmailMessageId = emailMessageId,
            SourceEmailAttachmentId = emailAttachmentId,
            SourceEmailSubject = context?.Subject,
            SourceEmailBodyText = context?.BodyText,
            SourceEmailBodyHtml = context?.BodyHtml,
        };
        var aiValidatedResponse = await pipeline.ExtractPricingDataAsync(
            normalizedRequest,
            cancellationToken
        );

        if (!IsUsable(aiValidatedResponse))
        {
            var error = aiValidatedResponse.ErrorMessage
                ?? "DataExtraction no pudo validar la salida estructurada de AI.";

            return new AutomatedPricingExtractionResult(
                WithAiError(aiValidatedResponse, error),
                true,
                false,
                analysis.AiExecutionId,
                analysis.Confidence,
                error
            );
        }

        return new AutomatedPricingExtractionResult(
            aiValidatedResponse,
            true,
            true,
            analysis.AiExecutionId,
            analysis.Confidence,
            null
        );
    }

    public async Task<AutomatedPricingExtractionResult> ExtractAsync(
        ExtractionDataRequest request,
        AutomatedPricingExtractionContext? context = null,
        CancellationToken cancellationToken = default
    )
    {
        request = ApplyEmailContext(request, context);

        if (!IsSupportedExtractionSource(request))
        {
            return WithoutAi(CreateUnsupportedSourceResponse(request));
        }

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
            false
        );

        if (!IsAiEnabled())
        {
            const string error = "La etapa opcional de normalización con AI está deshabilitada.";
            return IsUsable(deterministicResponse)
                ? WithoutAi(deterministicResponse)
                : RequiredAiFailure(deterministicResponse, error, aiAttempted: false);
        }

        var deterministicConfidence = CalculatePreviousConfidence(
            deterministicResponse
        );
        var minimumDeterministicConfidence = ReadPercentage(
            configuration[
                "AI:AutomaticExtraction:MinimumDeterministicConfidence"
            ],
            75m
        );
        var analyzeEverySource = ReadBoolean(
            configuration["AI:AutomaticExtraction:AnalyzeEverySource"],
            false
        );

        if (
            context?.ForceAiAnalysis != true
            && !analyzeEverySource
            && IsUsable(deterministicResponse)
            && deterministicConfidence >= minimumDeterministicConfidence
        )
        {
            logger.LogInformation(
                "Se omitió AI para {SourceName}: confianza determinística {Confidence:0.##}% "
                    + "sobre el umbral {Threshold:0.##}%.",
                request.OriginalFileName,
                deterministicConfidence,
                minimumDeterministicConfidence
            );
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

            var normalizedSubject = EmailSubjectNormalizer.NormalizeForExtraction(
                context?.Subject ?? request.SourceEmailSubject
            );
            var catalogHints = await BuildCatalogHintsAsync(
                deterministicResponse,
                BuildCatalogSearchContent(
                    normalizedSubject,
                    context?.BodyText ?? request.SourceEmailBodyText,
                    context?.BodyHtml ?? request.SourceEmailBodyHtml,
                    sourceContent
                ),
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
                        normalizedSubject,
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
                    SourceImageBase64: null,
                    SourceImageMimeType: null
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

            var normalizedAiRows = NormalizeAiRowsForEmailSemantics(
                analysis.Rows,
                context
            );
            var csvContent = BuildNormalizedCsv(normalizedAiRows, analysis.Warnings);
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
                SourceEmailSubject = context?.Subject ?? request.SourceEmailSubject,
                SourceEmailBodyText = context?.BodyText ?? request.SourceEmailBodyText,
                SourceEmailBodyHtml = context?.BodyHtml ?? request.SourceEmailBodyHtml,
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
                false
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

    private static ExtractPricingDataResponse CreateFailedAiResponse(
        Guid pricingImportId,
        string correlationId,
        string errorCode,
        string errorMessage
    )
    {
        return new ExtractPricingDataResponse(
            false,
            null,
            pricingImportId,
            correlationId,
            new ExtractionSummaryDto(0, 0, 0, 0, true),
            null,
            Array.Empty<ExtractedPricingRowDto>(),
            [
                new ExtractionIssueDto(
                    Guid.NewGuid(),
                    Guid.Empty,
                    null,
                    errorCode,
                    errorMessage,
                    true,
                    null,
                    null,
                    null,
                    null
                ),
            ],
            errorCode,
            errorMessage
        );
    }

    private static string ComputeSha256(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
    }

    private string? BuildLimitedEmailContext(
        string? bodyText,
        string? bodyHtml
    )
    {
        var value = EmailPricingContentSelector.SelectPreferredBody(
            bodyText,
            bodyHtml
        );
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return LimitPreservingEdges(
            value,
            ReadPositiveInt(
                configuration[
                    "AI:EmailFallback:MaximumEmailContextCharacters"
                ],
                8_000
            ),
            "\n[CONTEXTO INTERMEDIO OMITIDO]\n"
        );
    }

    private static string? StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var value = Regex.Replace(
            html,
            "<(script|style)[^>]*>.*?</\\1>",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );
        value = Regex.Replace(value, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
        value = Regex.Replace(
            value,
            "</(p|div|tr|li|h[1-6])>",
            "\n",
            RegexOptions.IgnoreCase
        );
        value = Regex.Replace(value, "</(td|th)>", "\t", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, "<[^>]+>", " ");
        return WebUtility.HtmlDecode(value);
    }

    private static ExtractionDataRequest ApplyEmailContext(
        ExtractionDataRequest request,
        AutomatedPricingExtractionContext? context
    )
    {
        if (context is null)
        {
            return request;
        }

        return request with
        {
            SourceEmailMessageId = context.EmailMessageId ?? request.SourceEmailMessageId,
            SourceEmailAttachmentId = context.EmailAttachmentId
                ?? request.SourceEmailAttachmentId,
            SourceEmailSubject = context.Subject ?? request.SourceEmailSubject,
            SourceEmailBodyText = context.BodyText ?? request.SourceEmailBodyText,
            SourceEmailBodyHtml = context.BodyHtml ?? request.SourceEmailBodyHtml,
        };
    }

    private static string BuildCatalogSearchContent(
        string? subject,
        string? bodyText,
        string? bodyHtml,
        string sourceContent
    )
    {
        var focusedEmail = EmailPricingContentSelector.SelectPreferredBody(
            bodyText,
            bodyHtml
        );
        return string.Join(
            '\n',
            new[]
            {
                subject,
                focusedEmail,
                sourceContent,
            }.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray()
        );
    }

    private static string LimitPreservingEdges(
        string value,
        int maximumCharacters,
        string marker
    )
    {
        if (value.Length <= maximumCharacters)
        {
            return value;
        }

        if (maximumCharacters <= marker.Length + 2)
        {
            return value[..maximumCharacters];
        }

        var availableCharacters = maximumCharacters - marker.Length;
        var headCharacters = availableCharacters * 3 / 4;
        var tailCharacters = availableCharacters - headCharacters;
        return value[..headCharacters] + marker + value[^tailCharacters..];
    }

    private async Task<IReadOnlyCollection<AiCatalogGroupHint>> BuildCatalogHintsAsync(
        ExtractPricingDataResponse response,
        string sourceContent,
        CancellationToken cancellationToken
    )
    {
        var hints = new List<AiCatalogGroupHint>();
        var normalizedSource = NormalizeSearchText(sourceContent);
        var catalogGroups = await Task.WhenAll(
            PricingCatalogSlugs.RowCatalogs.Select(async groupSlug =>
            {
                try
                {
                    var items = await configCatalogClient.GetActiveCatalogItemsByGroupAsync(
                        groupSlug,
                        cancellationToken
                    );
                    return (GroupSlug: groupSlug, Items: items);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogWarning(
                        exception,
                        "No se pudieron cargar sugerencias del catálogo {CatalogGroup} para AI.",
                        groupSlug
                    );
                    return (
                        GroupSlug: groupSlug,
                        Items: (IReadOnlyCollection<ConfigCatalogItemResult>)
                            Array.Empty<ConfigCatalogItemResult>()
                    );
                }
            })
        );

        foreach (var catalogGroup in catalogGroups)
        {
            var searchTerms = GetPreviousValues(response, catalogGroup.GroupSlug)
                .Select(NormalizeSearchText)
                .Where(value => value.Length >= 2)
                .ToArray();
            var selected = catalogGroup.Items
                .Where(item => item.IsActive)
                .Select(item => new
                {
                    Item = item,
                    Score = ScoreCatalogItem(item, searchTerms, normalizedSource),
                })
                .Where(result => result.Score > 0)
                .OrderByDescending(result => result.Score)
                .ThenBy(result => result.Item.Name)
                .Take(GetCatalogHintLimit(catalogGroup.GroupSlug))
                .Select(result => new AiCatalogItemHint(
                    result.Item.Code,
                    result.Item.Slug,
                    result.Item.Name,
                    result.Item.Value
                ))
                .ToArray();

            if (selected.Length > 0)
            {
                hints.Add(new AiCatalogGroupHint(catalogGroup.GroupSlug, selected));
            }
        }

        return hints;
    }

    private static int GetCatalogHintLimit(string groupSlug)
    {
        return groupSlug switch
        {
            PricingCatalogSlugs.Pol or PricingCatalogSlugs.Poe or PricingCatalogSlugs.Pod => 20,
            PricingCatalogSlugs.Agents => 12,
            PricingCatalogSlugs.Carriers => 12,
            _ => 10,
        };
    }

    private static IReadOnlyCollection<AiPricingEmailRow> BuildPreviousRows(
        ExtractPricingDataResponse response
    )
    {
        return response.Rows
            .Take(MaximumPreviousRows)
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
            .Take(MaximumPreviousIssues)
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
            var reference = groupSlug switch
            {
                PricingCatalogSlugs.Pol => row.OriginPortReference,
                PricingCatalogSlugs.Poe => row.PortOfExitReference,
                PricingCatalogSlugs.Pod => row.DestinationPortReference,
                PricingCatalogSlugs.ContainerTypes => row.ContainerTypeReference,
                PricingCatalogSlugs.Carriers => row.CarrierReference,
                PricingCatalogSlugs.Agents => row.AgentReference,
                PricingCatalogSlugs.Currencies => row.CurrencyReference,
                _ => null,
            };

            if (!string.IsNullOrWhiteSpace(value))
            {
                yield return value;
            }

            if (reference is not null)
            {
                yield return reference.Name;
                yield return reference.Code;
                yield return reference.Slug;
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

    private static bool IsSupportedExtractionSource(
        ExtractionDataRequest request
    )
    {
        var extension = NormalizeExtension(request.FileExtension);
        var sourceOriginType = request.SourceOriginType?.Trim();

        if (
            string.Equals(
                sourceOriginType,
                "EmailBody",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            return extension is ".html" or ".txt"
                || request.ContentType is "text/html" or "text/plain";
        }

        return extension is ".pdf" or ".csv" or ".xlsx";
    }

    private static ExtractPricingDataResponse CreateUnsupportedSourceResponse(
        ExtractionDataRequest request
    )
    {
        return CreateFailedAiResponse(
            request.PricingImportId,
            request.CorrelationId,
            "DataExtraction.UnsupportedSourceType",
            "El formato no se procesa. DataExtraction solo admite cuerpo de correo, PDF, CSV o XLSX; las imágenes y demás archivos únicamente se almacenan."
        );
    }

    private static string NormalizeExtension(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var extension = value.Trim().ToLowerInvariant();
        return extension.StartsWith('.') ? extension : $".{extension}";
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
        var structuralConfidence = Math.Clamp(
            decimal.Divide(usableRows, response.Summary.TotalRows) * 100m,
            0m,
            100m
        );
        var normalizationConfidence = CalculateCatalogNormalizationConfidence(
            response
        );

        return Math.Min(structuralConfidence, normalizationConfidence);
    }

    private static decimal CalculateCatalogNormalizationConfidence(
        ExtractPricingDataResponse response
    )
    {
        if (response.Rows.Count == 0)
        {
            return 0m;
        }

        decimal accumulated = 0m;
        foreach (var row in response.Rows)
        {
            var expected = 5m;
            var matched = 0m;

            matched += row.OriginPortReference is not null ? 1m : 0m;
            matched += row.PortOfExitReference is not null ? 1m : 0m;
            matched += row.ContainerTypeReference is not null ? 1m : 0m;
            matched += row.CarrierReference is not null ? 1m : 0m;
            matched += row.CurrencyReference is not null ? 1m : 0m;

            if (!string.IsNullOrWhiteSpace(row.DestinationPort))
            {
                expected++;
                matched += row.DestinationPortReference is not null ? 1m : 0m;
            }

            if (!string.IsNullOrWhiteSpace(row.Agent))
            {
                expected++;
                matched += row.AgentReference is not null ? 1m : 0m;
            }

            accumulated += decimal.Divide(matched, expected) * 100m;
        }

        return Math.Clamp(
            decimal.Divide(accumulated, response.Rows.Count),
            0m,
            100m
        );
    }


    private static IReadOnlyCollection<AiPricingEmailRow> NormalizeAiRowsForEmailSemantics(
        IReadOnlyCollection<AiPricingEmailRow> rows,
        AutomatedPricingExtractionContext? context
    )
    {
        var rebuiltNarrativeRows = TryRebuildWwlNarrativeNacRows(rows, context);
        if (rebuiltNarrativeRows.Count > 0)
        {
            return rebuiltNarrativeRows;
        }

        rows = RepairMissingValidityFromEmailSource(rows, context);

        var promotePodToPoe = ShouldPromoteDestinationPortToPortOfExit(context);
        var inferredContainerType = ResolveNarrativeNacContainerType(context);

        return rows
            .Select(row =>
            {
                var portOfExit = row.PortOfExit;
                var destinationPort = row.DestinationPort;
                var remarks = row.Remarks;

                if (promotePodToPoe && HasValue(destinationPort))
                {
                    // Regla de negocio para importaciones por correo: el campo POD
                    // de la tarifa representa Port of Discharge y se persiste como
                    // POE. Dhole no debe conservarlo como destino final.
                    portOfExit = destinationPort;
                    destinationPort = null;
                    remarks = JoinRemarks(
                        remarks,
                        "POE recuperado desde POD marítimo (Port of Discharge)."
                    );
                }

                var containerType = row.ContainerType;
                if (
                    !HasValue(containerType)
                    && HasValue(inferredContainerType)
                )
                {
                    containerType = inferredContainerType;
                    remarks = JoinRemarks(
                        remarks,
                        $"Equipo {inferredContainerType} inferido para oferta contractual narrativa MSC/ONE NAC."
                    );
                }

                return row with
                {
                    PortOfExit = portOfExit,
                    DestinationPort = destinationPort,
                    ContainerType = containerType,
                    Remarks = remarks,
                };
            })
            .ToArray();
    }

    /// <summary>
    /// AI providers occasionally return a complete WWL/forwarder matrix but omit the
    /// validity dates even though the source contains a clear "Validity (ETD)" column.
    /// Re-read the newest deterministic matrix and recover those dates before the AI
    /// rows are converted to CSV. This keeps Pricing from receiving an entire expanded
    /// batch with missing_valid_from / missing_valid_to.
    /// </summary>
    internal static IReadOnlyCollection<AiPricingEmailRow> RepairMissingValidityFromEmailSource(
        IReadOnlyCollection<AiPricingEmailRow> rows,
        AutomatedPricingExtractionContext? context
    )
    {
        if (
            context is null
            || rows.Count == 0
            || rows.All(row => row.ValidFrom.HasValue && row.ValidTo.HasValue)
        )
        {
            return rows;
        }

        var source = BuildEmailSemanticSource(context);
        if (string.IsNullOrWhiteSpace(source))
        {
            return rows;
        }

        var tables = EmailDocumentExtractor.TryExtractStackedFclTablesFromText(source);
        if (tables.Count == 0)
        {
            return rows;
        }

        var referenceYear = ResolveEmailReferenceYear(context);
        var offers = tables
            .SelectMany(table => table.Rows)
            .Select(extractedRow => BuildEmailValidityOffer(extractedRow.Values, referenceYear))
            .Where(offer => offer is not null)
            .Cast<EmailValidityOffer>()
            .ToArray();

        if (offers.Length == 0)
        {
            return rows;
        }

        return rows
            .Select(row =>
            {
                if (row.ValidFrom.HasValue && row.ValidTo.HasValue)
                {
                    return row;
                }

                var offer = offers
                    .Select(candidate => new
                    {
                        Candidate = candidate,
                        Score = ScoreValidityOffer(row, candidate),
                    })
                    .Where(candidate => candidate.Score > 0)
                    .OrderByDescending(candidate => candidate.Score)
                    .Select(candidate => candidate.Candidate)
                    .FirstOrDefault();

                if (offer is null)
                {
                    return row;
                }

                var validFrom = row.ValidFrom ?? offer.ValidFrom;
                var validTo = row.ValidTo ?? offer.ValidTo;
                var remarks = JoinRemarks(
                    row.Remarks,
                    "Vigencia recuperada directamente de Validity (ETD) del correo."
                );

                return row with
                {
                    ValidFrom = validFrom,
                    ValidTo = validTo,
                    Remarks = remarks,
                };
            })
            .ToArray();
    }

    private static EmailValidityOffer? BuildEmailValidityOffer(
        IReadOnlyDictionary<string, string?> values,
        int referenceYear
    )
    {
        var rawCarrier = GetExtractedValue(values, "Carrier");
        var normalizedCarrier = CarrierNameNormalizer.Normalize(rawCarrier);
        var rawFrom = GetExtractedValue(values, "ValidFrom");
        var rawTo = GetExtractedValue(values, "ValidTo");
        var validFrom = ParseEmailSourceDate(rawFrom, referenceYear);
        var validTo = ParseEmailSourceDate(rawTo, referenceYear);

        if (
            !HasValue(normalizedCarrier)
            || !validFrom.HasValue
            || !validTo.HasValue
        )
        {
            return null;
        }

        // Validity ranges can cross New Year (for example 29 Dec-4 Jan).
        if (validTo.Value.Date < validFrom.Value.Date)
        {
            validTo = validTo.Value.AddYears(1);
        }

        return new EmailValidityOffer(
            normalizedCarrier!,
            rawCarrier,
            GetExtractedValue(values, "POL"),
            GetExtractedValue(values, "POE"),
            validFrom.Value.Date,
            validTo.Value.Date
        );
    }

    private static DateTime? ParseEmailSourceDate(string? value, int referenceYear)
    {
        if (!HasValue(value))
        {
            return null;
        }

        var clean = Regex.Replace(value!.Trim(), @"\s+", " ");
        if (!Regex.IsMatch(clean, @"\b(?:19|20)\d{2}\b"))
        {
            clean = $"{clean} {referenceYear}";
        }

        return DateNormalizer.Normalize(clean);
    }

    private static int ResolveEmailReferenceYear(AutomatedPricingExtractionContext context)
    {
        foreach (var source in new[] { context.BodyText, context.BodyHtml, context.Subject })
        {
            if (!HasValue(source))
            {
                continue;
            }

            var match = Regex.Match(
                source!,
                @"(?:Enviado|Sent|Date|Fecha)\s*:\s*[^\r\n]{0,160}\b(?<year>20\d{2})\b",
                RegexOptions.IgnoreCase
            );
            if (
                match.Success
                && int.TryParse(
                    match.Groups["year"].Value,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var parsedYear
                )
            )
            {
                return parsedYear;
            }
        }

        return DateTime.UtcNow.Year;
    }

    private static int ScoreValidityOffer(AiPricingEmailRow row, EmailValidityOffer offer)
    {
        var rowCarrier = CarrierNameNormalizer.Normalize(row.Carrier);
        if (
            !HasValue(rowCarrier)
            || !string.Equals(rowCarrier, offer.Carrier, StringComparison.OrdinalIgnoreCase)
        )
        {
            return 0;
        }

        var score = 100;
        if (HasValue(row.Carrier) && HasValue(offer.RawCarrier))
        {
            var rowProduct = ExtractCarrierProductToken(row.Carrier!);
            var offerProduct = ExtractCarrierProductToken(offer.RawCarrier!);
            if (
                HasValue(rowProduct)
                && HasValue(offerProduct)
                && string.Equals(rowProduct, offerProduct, StringComparison.OrdinalIgnoreCase)
            )
            {
                score += 40;
            }
        }

        if (RouteVariantMatches(offer.OriginPort, row.OriginPort))
        {
            score += 25;
        }

        if (RouteVariantMatches(offer.PortOfExit, row.PortOfExit ?? row.DestinationPort))
        {
            score += 15;
        }

        return score;
    }

    private static string? ExtractCarrierProductToken(string value)
    {
        var match = Regex.Match(
            value,
            @"\b(FAK|BASKET|SPOT|PREMIUM)\b",
            RegexOptions.IgnoreCase
        );
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
    }

    private static bool RouteVariantMatches(string? sourceVariants, string? rowValue)
    {
        if (!HasValue(sourceVariants) || !HasValue(rowValue))
        {
            return false;
        }

        var rowNormalized = ColumnHeaderNormalizer.Normalize(rowValue!);
        if (string.IsNullOrWhiteSpace(rowNormalized))
        {
            return false;
        }

        return sourceVariants!
            .Split(['/', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ColumnHeaderNormalizer.Normalize)
            .Any(variant =>
                !string.IsNullOrWhiteSpace(variant)
                && (
                    string.Equals(variant, rowNormalized, StringComparison.OrdinalIgnoreCase)
                    || rowNormalized.Contains(variant, StringComparison.OrdinalIgnoreCase)
                    || variant.Contains(rowNormalized, StringComparison.OrdinalIgnoreCase)
                )
            );
    }

    private sealed record EmailValidityOffer(
        string Carrier,
        string? RawCarrier,
        string? OriginPort,
        string? PortOfExit,
        DateTime ValidFrom,
        DateTime ValidTo
    );

    private static IReadOnlyCollection<AiPricingEmailRow> TryRebuildWwlNarrativeNacRows(
        IReadOnlyCollection<AiPricingEmailRow> aiRows,
        AutomatedPricingExtractionContext? context
    )
    {
        if (context is null)
        {
            return Array.Empty<AiPricingEmailRow>();
        }

        var source = BuildEmailSemanticSource(context);
        if (
            !source.Contains("Below the details of ONE NAC", StringComparison.OrdinalIgnoreCase)
            && !source.Contains("ONE NAC must match COMM", StringComparison.OrdinalIgnoreCase)
        )
        {
            return Array.Empty<AiPricingEmailRow>();
        }

        var table = EmailDocumentExtractor.TryExtractNarrativeNacTable(source);
        if (table is null || table.Rows.Count == 0)
        {
            return Array.Empty<AiPricingEmailRow>();
        }

        var referenceDate = aiRows
            .Select(row => row.ValidFrom ?? row.ValidTo)
            .FirstOrDefault(value => value.HasValue)
            ?.Date
            ?? DateTime.UtcNow.Date;
        var agent = aiRows
            .Select(row => row.Agent)
            .FirstOrDefault(HasValue);
        if (!HasValue(agent)
            && context.Subject?.Contains("WWL", StringComparison.OrdinalIgnoreCase) == true)
        {
            agent = "WWL";
        }

        var rebuilt = new List<AiPricingEmailRow>();
        foreach (var extractedRow in table.Rows)
        {
            var values = extractedRow.Values;
            var pol = GetExtractedValue(values, "POL");
            var poe = GetExtractedValue(values, "POE");
            var carrier = GetExtractedValue(values, "Carrier");
            var containerType = GetExtractedValue(values, "ContainerSize");
            var oceanFreight = ParseExtractedDecimal(
                GetExtractedValue(values, "FreightAmount")
            );
            var validFrom = ParseExtractedNarrativeDate(
                GetExtractedValue(values, "ValidFrom"),
                referenceDate
            );
            var validTo = ParseExtractedNarrativeDate(
                GetExtractedValue(values, "ValidTo"),
                referenceDate
            );
            if (
                !HasValue(pol)
                || !HasValue(poe)
                || !HasValue(carrier)
                || !HasValue(containerType)
                || !oceanFreight.HasValue
                || !validFrom.HasValue
                || !validTo.HasValue
            )
            {
                continue;
            }

            rebuilt.Add(new AiPricingEmailRow(
                pol,
                poe,
                null,
                containerType,
                carrier,
                agent,
                GetExtractedValue(values, "Commodity"),
                FirstNotEmpty(GetExtractedValue(values, "Currency"), "USD"),
                ParseExtractedInt(GetExtractedValue(values, "FreeDays")),
                null,
                validFrom,
                validTo,
                oceanFreight,
                ParseExtractedDecimal(GetExtractedValue(values, "OriginCharges")),
                null,
                ParseExtractedDecimal(GetExtractedValue(values, "Surcharges")),
                null,
                null,
                null,
                null,
                null,
                JoinRemarks(
                    GetExtractedValue(values, "Remarks"),
                    "Matriz MSC/ONE NAC reconstruida desde el correo; POD de la fuente almacenado como POE."
                )
            ));
        }

        return rebuilt;
    }

    private static string? GetExtractedValue(
        IReadOnlyDictionary<string, string?> values,
        string key
    )
    {
        return values.TryGetValue(key, out var value) && HasValue(value)
            ? value!.Trim()
            : null;
    }

    private static decimal? ParseExtractedDecimal(string? value)
    {
        if (!HasValue(value))
        {
            return null;
        }

        var normalized = Regex.Replace(value!, @"[^0-9.\-]", string.Empty);
        return decimal.TryParse(
            normalized,
            NumberStyles.Number | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var parsed
        )
            ? parsed
            : null;
    }

    private static int? ParseExtractedInt(string? value)
    {
        return int.TryParse(
            value,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var parsed
        )
            ? parsed
            : null;
    }

    private static DateTime? ParseExtractedNarrativeDate(
        string? value,
        DateTime referenceDate
    )
    {
        if (!HasValue(value))
        {
            return null;
        }

        var normalized = Regex.Replace(value!.Trim(), @"\s+", " ");
        if (!Regex.IsMatch(normalized, @"\b\d{4}\b"))
        {
            normalized = $"{normalized} {referenceDate.Year}";
        }

        string[] formats =
        [
            "d MMM yyyy",
            "dd MMM yyyy",
            "d MMMM yyyy",
            "dd MMMM yyyy",
            "yyyy-MM-dd",
        ];
        if (!DateTime.TryParseExact(
            normalized,
            formats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var parsed
        ))
        {
            return null;
        }

        return DateTime.SpecifyKind(parsed.Date, DateTimeKind.Unspecified);
    }

    private static bool ShouldPromoteDestinationPortToPortOfExit(
        AutomatedPricingExtractionContext? context
    )
    {
        if (context is null)
        {
            return false;
        }

        if (
            !string.IsNullOrWhiteSpace(context.SourceType)
            && context.SourceType.Contains("Email", StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        var source = BuildEmailSemanticSource(context);
        return Regex.IsMatch(
            source,
            @"\b(?:POD|Port\s+of\s+Discharge)\b\s*:?[ \t]*",
            RegexOptions.IgnoreCase
        );
    }

    private static string? ResolveNarrativeNacContainerType(
        AutomatedPricingExtractionContext? context
    )
    {
        if (context is null)
        {
            return null;
        }

        var source = BuildEmailSemanticSource(context);
        var isNarrativeNac = source.Contains("NAC", StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(
                source,
                @"\b(?:pls|please)\s+consider\s+(?:the\s+)?rate\b",
                RegexOptions.IgnoreCase
            )
            && Regex.IsMatch(source, @"\bPOL\s*:\s*\S+", RegexOptions.IgnoreCase)
            && Regex.IsMatch(source, @"\bPOD\s*:\s*\S+", RegexOptions.IgnoreCase);
        if (!isNarrativeNac)
        {
            return null;
        }

        var equipmentMatch = Regex.Match(
            source,
            @"\b(?<size>20|40|45)\s*['’]?\s*(?<type>GP|DV|DC|STD|ST|HC|HQ|NOR|RF)?\b",
            RegexOptions.IgnoreCase
        );
        if (!equipmentMatch.Success)
        {
            return "40HC";
        }

        var size = equipmentMatch.Groups["size"].Value;
        var type = equipmentMatch.Groups["type"].Value.ToUpperInvariant();
        return size switch
        {
            "20" => type is "HC" or "HQ" ? "20HC" : "20DV",
            "45" => "45HC",
            _ => type is "HC" or "HQ" ? "40HC" : "40DV",
        };
    }

    private static string BuildEmailSemanticSource(
        AutomatedPricingExtractionContext context
    )
    {
        var source = string.Join(
            "\n",
            new[]
            {
                context.Subject,
                context.BodyText,
                string.IsNullOrWhiteSpace(context.BodyHtml)
                    ? null
                    : EmailPricingContentSelector.NormalizeHtml(context.BodyHtml),
            }.Where(value => !string.IsNullOrWhiteSpace(value))
        );

        return EmailPricingContentSelector.SelectNewestPricingSection(source);
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
            ErrorMessage = "AI no pudo mejorar la normalización determinística antes de enviar los datos a Pricing. "
                + error.Trim()
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

    private static decimal ReadPercentage(string? value, decimal fallback)
    {
        return decimal.TryParse(
            value,
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var parsed
        )
            ? Math.Clamp(parsed, 0m, 100m)
            : Math.Clamp(fallback, 0m, 100m);
    }

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(
                value,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var parsed
            )
            && parsed > 0
            ? parsed
            : fallback;
    }
}
