from pathlib import Path
import re

SERVICE = Path('src/Dhole.DataExtraction.Infrastructure/Pipeline/AutomatedPricingExtractionService.cs')
TESTS = Path('tests/Dhole.DataExtraction.UnitTests/AutomatedPricingExtractionServiceTests.cs')

service = SERVICE.read_text(encoding='utf-8')

old_intro = '''        var deterministicResponse = await pipeline.ExtractPricingDataAsync(\n            request,\n            cancellationToken\n        );\n\n        if (IsAiGeneratedRequest(request))\n        {\n            return WithoutAi(deterministicResponse);\n        }\n\n        var requireAiResult = ReadBoolean(\n'''

new_intro = '''        var deterministicTask = pipeline.ExtractPricingDataAsync(\n            request,\n            cancellationToken\n        );\n\n        if (IsAiGeneratedRequest(request))\n        {\n            return WithoutAi(await deterministicTask);\n        }\n\n        var requireAiResult = ReadBoolean(\n'''
if old_intro not in service:
    raise SystemExit('Could not find extraction intro block')
service = service.replace(old_intro, new_intro, 1)

old_disabled = '''        if (!IsAiEnabled())\n        {\n            const string error = "La etapa opcional de normalización con AI está deshabilitada.";\n            return IsUsable(deterministicResponse)\n                ? WithoutAi(deterministicResponse)\n                : RequiredAiFailure(deterministicResponse, error, aiAttempted: false);\n        }\n\n        var deterministicConfidence = CalculatePreviousConfidence(\n            deterministicResponse\n        );\n'''

new_disabled = '''        if (!IsAiEnabled())\n        {\n            const string error = "La etapa opcional de normalización con AI está deshabilitada.";\n            var deterministicResponse = await deterministicTask;\n            return IsUsable(deterministicResponse)\n                ? WithoutAi(deterministicResponse)\n                : RequiredAiFailure(deterministicResponse, error, aiAttempted: false);\n        }\n\n        if (IsParallelFileSource(request))\n        {\n            return await ExtractParallelFileAsync(\n                request,\n                context,\n                requireAiResult,\n                deterministicTask,\n                cancellationToken\n            );\n        }\n\n        var deterministicResponse = await deterministicTask;\n        var deterministicConfidence = CalculatePreviousConfidence(\n            deterministicResponse\n        );\n'''
if old_disabled not in service:
    raise SystemExit('Could not find AI disabled block')
service = service.replace(old_disabled, new_disabled, 1)

marker = '    private static ExtractPricingDataResponse CreateFailedAiResponse(\n'
helper = r'''    private async Task<AutomatedPricingExtractionResult> ExtractParallelFileAsync(
        ExtractionDataRequest request,
        AutomatedPricingExtractionContext? context,
        bool requireAiResult,
        Task<ExtractPricingDataResponse> deterministicTask,
        CancellationToken cancellationToken
    )
    {
        // The deterministic extractor and AI parser start independently. Neither waits
        // for the other to produce rows. This is intentional for PDF/CSV/XLSX because
        // one parser must remain usable when the other fails, times out or returns zero rows.
        var sourceContentTask = contentReader.ReadAsTextAsync(
            request.OriginalFileName,
            request.ContentType,
            request.FileExtension,
            request.FileContent,
            cancellationToken
        );
        var sourceType = FirstNotEmpty(
            request.SourceOriginType,
            context?.SourceType,
            "ManualUpload"
        )!;
        var aiTask = AnalyzeAiAsync(
            request,
            context,
            sourceType,
            sourceContentTask,
            cancellationToken
        );

        await Task.WhenAll(deterministicTask, aiTask);
        var deterministicResponse = deterministicTask.Result;
        var aiAttempt = aiTask.Result;

        if (
            aiAttempt.Analysis is null
            || !aiAttempt.Analysis.Success
            || aiAttempt.Analysis.Rows.Count == 0
        )
        {
            var error = aiAttempt.ErrorMessage
                ?? aiAttempt.Analysis?.ErrorMessage
                ?? aiAttempt.Analysis?.Warnings.FirstOrDefault()
                ?? "AI no devolvió filas de tarifas utilizables.";

            // For supported tariff files AI is enrichment, not a hard dependency.
            // A valid deterministic matrix must continue to Pricing even when AI fails.
            if (IsUsable(deterministicResponse))
            {
                logger.LogWarning(
                    "AI no produjo filas utilizables para el adjunto {SourceName}; "
                        + "se conserva la extracción determinística. Error: {Error}",
                    request.OriginalFileName,
                    error
                );

                return new AutomatedPricingExtractionResult(
                    deterministicResponse,
                    true,
                    false,
                    aiAttempt.AiExecutionId,
                    aiAttempt.Confidence,
                    error
                );
            }

            return requireAiResult
                ? RequiredAiFailure(
                    deterministicResponse,
                    error,
                    aiAttempt.AiExecutionId,
                    aiAttempt.Confidence
                )
                : new AutomatedPricingExtractionResult(
                    WithAiError(deterministicResponse, error),
                    true,
                    false,
                    aiAttempt.AiExecutionId,
                    aiAttempt.Confidence,
                    error
                );
        }

        var analysis = aiAttempt.Analysis;
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

        ExtractPricingDataResponse aiValidatedResponse;
        try
        {
            aiValidatedResponse = await pipeline.ExtractPricingDataAsync(
                normalizedRequest,
                cancellationToken
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
                "La salida de AI no pudo revalidarse para el adjunto {SourceName}; "
                    + "se conserva la extracción determinística.",
                request.OriginalFileName
            );

            if (IsUsable(deterministicResponse))
            {
                return new AutomatedPricingExtractionResult(
                    deterministicResponse,
                    true,
                    false,
                    aiAttempt.AiExecutionId,
                    aiAttempt.Confidence,
                    exception.Message
                );
            }

            return requireAiResult
                ? RequiredAiFailure(
                    deterministicResponse,
                    exception.Message,
                    aiAttempt.AiExecutionId,
                    aiAttempt.Confidence
                )
                : new AutomatedPricingExtractionResult(
                    WithAiError(deterministicResponse, exception.Message),
                    true,
                    false,
                    aiAttempt.AiExecutionId,
                    aiAttempt.Confidence,
                    exception.Message
                );
        }

        if (!IsUsable(aiValidatedResponse))
        {
            var error = aiValidatedResponse.ErrorMessage
                ?? "DataExtraction no pudo validar la salida estructurada de AI.";

            if (IsUsable(deterministicResponse))
            {
                logger.LogWarning(
                    "DataExtraction rechazó la salida de AI para el adjunto {SourceName}; "
                        + "se conserva la extracción determinística. Error: {Error}",
                    request.OriginalFileName,
                    error
                );

                return new AutomatedPricingExtractionResult(
                    deterministicResponse,
                    true,
                    false,
                    aiAttempt.AiExecutionId,
                    aiAttempt.Confidence,
                    error
                );
            }

            return requireAiResult
                ? RequiredAiFailure(
                    deterministicResponse,
                    error,
                    aiAttempt.AiExecutionId,
                    aiAttempt.Confidence
                )
                : new AutomatedPricingExtractionResult(
                    WithAiError(deterministicResponse, error),
                    true,
                    false,
                    aiAttempt.AiExecutionId,
                    aiAttempt.Confidence,
                    error
                );
        }

        var useAiResponse = ReadBoolean(
            configuration["AI:AutomaticExtraction:PreferAiResult"],
            false
        )
            ? true
            : ShouldSelectAiResponse(deterministicResponse, aiValidatedResponse);

        var selectedResponse = useAiResponse
            ? aiValidatedResponse
            : deterministicResponse;

        logger.LogInformation(
            "Extracción paralela completada para {SourceName}. AI intentada: true; "
                + "AI seleccionada: {AiApplied}; filas determinísticas: {DeterministicRows}; "
                + "filas AI validadas: {AiRows}; ejecución AI: {AiExecutionId}.",
            request.OriginalFileName,
            useAiResponse,
            deterministicResponse.Rows.Count,
            aiValidatedResponse.Rows.Count,
            aiAttempt.AiExecutionId
        );

        return new AutomatedPricingExtractionResult(
            selectedResponse,
            true,
            useAiResponse,
            aiAttempt.AiExecutionId,
            aiAttempt.Confidence,
            null
        );
    }

    private async Task<AiAnalysisAttempt> AnalyzeAiAsync(
        ExtractionDataRequest request,
        AutomatedPricingExtractionContext? context,
        string sourceType,
        Task<string> sourceContentTask,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var sourceContent = await sourceContentTask;
            var normalizedSubject = EmailSubjectNormalizer.NormalizeForExtraction(
                context?.Subject ?? request.SourceEmailSubject
            );
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
                    null,
                    null,
                    0m,
                    Array.Empty<AiPricingEmailRow>(),
                    Array.Empty<AiPreviousExtractionIssue>(),
                    Array.Empty<AiCatalogGroupHint>(),
                    SourceImageBase64: null,
                    SourceImageMimeType: null
                ),
                cancellationToken
            );

            return new AiAnalysisAttempt(
                analysis,
                analysis.AiExecutionId,
                analysis.Confidence,
                analysis.ErrorMessage ?? analysis.Warnings.FirstOrDefault()
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new AiAnalysisAttempt(null, null, null, exception.Message);
        }
    }

    private static bool IsParallelFileSource(ExtractionDataRequest request)
    {
        return NormalizeExtension(request.FileExtension) is ".pdf" or ".csv" or ".xlsx";
    }

    private sealed record AiAnalysisAttempt(
        AiPricingEmailAnalysisResult? Analysis,
        Guid? AiExecutionId,
        decimal? Confidence,
        string? ErrorMessage
    );

'''

if marker not in service:
    raise SystemExit('Could not find service insertion marker')
service = service.replace(marker, helper + marker, 1)
SERVICE.write_text(service, encoding='utf-8')

tests = TESTS.read_text(encoding='utf-8')
pattern = re.compile(
    r'    \[TestMethod\]\n    public async Task ManualUpload_WhenAiFails_DoesNotAllowDeterministicResultToReachPricing\(\)\n    \{.*?\n    \}\n\n    \[TestMethod\]',
    re.S,
)
replacement = '''    [TestMethod]
    public async Task ManualUpload_WhenAiFails_KeepsDeterministicResult()
    {
        var pricingImportId = Guid.NewGuid();
        var pipeline = new RecordingPipeline(Success(pricingImportId));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AI:AutomaticExtraction:Enabled"] = "true",
                    ["AI:AutomaticExtraction:AnalyzeEverySource"] = "true",
                    ["AI:AutomaticExtraction:RequireAiResult"] = "true",
                }
            )
            .Build();
        var service = new AutomatedPricingExtractionService(
            pipeline,
            new FailingAiClient(),
            new FakeContentReader(),
            new EmptyConfigCatalogClient(),
            configuration,
            NullLogger<AutomatedPricingExtractionService>.Instance
        );
        var content = Encoding.UTF8.GetBytes("tarifa marítima adjunta");
        var request = new ExtractionDataRequest(
            pricingImportId,
            "manual-ai-required-test",
            "tarifa.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xlsx",
            content.LongLength,
            "hash",
            "fcl-default",
            Guid.NewGuid(),
            "Maurice",
            content
        );

        var result = await service.ExtractAsync(request);

        Assert.IsTrue(result.AiAttempted);
        Assert.IsFalse(result.AiApplied);
        Assert.IsTrue(result.Response.Success);
        Assert.HasCount(1, pipeline.Requests);
        Assert.IsNotNull(result.AiErrorMessage);
    }

    [TestMethod]
'''
tests, count = pattern.subn(replacement, tests, count=1)
if count != 1:
    raise SystemExit('Could not replace AI failure regression test')
TESTS.write_text(tests, encoding='utf-8')
