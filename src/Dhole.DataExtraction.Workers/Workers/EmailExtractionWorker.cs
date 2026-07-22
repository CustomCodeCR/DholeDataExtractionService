using System.Globalization;
using System.Text;
using CustomCodeFramework.Workers.Abstractions;
using Dhole.DataExtraction.Application.Abstractions.Emails;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Contracts.Extraction;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using Dhole.DataExtraction.Infrastructure.Files;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Workers.Workers;

internal sealed class EmailExtractionWorker(
    ServiceDbContext dbContext,
    IEmailFileStorage fileStorage,
    IExtractionPipeline extractionPipeline,
    IEmailRateClassifier classifier,
    IPricingImportClient pricingImportClient,
    IAiExtractionClient aiExtractionClient,
    IAiEmailContentReader aiEmailContentReader,
    IConfiguration configuration,
    ILogger<EmailExtractionWorker> logger
) : IBackgroundWorker
{
    public string Name => "data-extraction.email-extraction";

    public async Task ExecuteAsync(IWorkerExecutionContext context, CancellationToken cancellationToken)
    {
        var maxJobs = ReadPositiveInt(configuration["EmailIngestion:MaxExtractionJobsPerRun"], 10);

        var jobs = await dbContext.EmailExtractionJobs
            .Where(x => x.Status == EmailExtractionJobStatus.Pending && !x.IsDeleted)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(maxJobs)
            .ToListAsync(cancellationToken);

        foreach (var job in jobs)
        {
            await ProcessJobAsync(job, cancellationToken);
        }
    }

    private async Task ProcessJobAsync(EmailExtractionJob job, CancellationToken cancellationToken)
    {
        EmailMessage? message = null;
        EmailAttachment? attachment = null;

        try
        {
            job.MarkProcessing();
            await dbContext.SaveChangesAsync(cancellationToken);

            message = await dbContext.EmailMessages.FirstOrDefaultAsync(
                x => x.Id == job.EmailMessageId && !x.IsDeleted,
                cancellationToken
            );

            if (message is null)
            {
                job.MarkFailed(null, "No se encontró el correo asociado al trabajo.");
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var account = await dbContext.EmailIngestionAccounts.FirstOrDefaultAsync(
                x => x.Id == message.EmailIngestionAccountId && !x.IsDeleted,
                cancellationToken
            );

            if (account is null)
            {
                job.MarkFailed(null, "No se encontró la cuenta de correo asociada al mensaje.");
                message.MarkFailed("No se encontró la cuenta de correo asociada al mensaje.");
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            if (
                job.SourceType == EmailContentSourceType.Body
                && !account.ProcessBodyEvenWithAttachments
                && await HasProcessableAttachmentAsync(message.Id, cancellationToken)
            )
            {
                job.MarkIgnored(
                    "Se omitió el cuerpo porque el correo contiene un adjunto soportado y la cuenta no permite procesar ambos formatos."
                );
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var input = await BuildExtractionInputAsync(job, message, cancellationToken);
            attachment = input.Attachment;
            message.MarkProcessing();

            var response = await extractionPipeline.ExtractPricingDataAsync(
                input.Request,
                cancellationToken
            );
            var confidence = classifier.CalculateExtractionConfidence(
                response,
                message,
                attachment
            );
            var usedAiFallback = false;
            Guid? aiExecutionId = null;
            string? aiFallbackError = null;

            if (ShouldUseAiFallback(response, confidence, account.AutoSendMinConfidence))
            {
                var aiFallback = await TryAiFallbackAsync(
                    job,
                    message,
                    input,
                    response,
                    confidence,
                    cancellationToken
                );

                if (aiFallback.Applied)
                {
                    response = aiFallback.Response;
                    confidence = aiFallback.Confidence;
                    usedAiFallback = true;
                    aiExecutionId = aiFallback.AiExecutionId;
                }
                else
                {
                    aiFallbackError = aiFallback.ErrorMessage
                        ?? "AI no devolvió filas de tarifas utilizables.";
                    logger.LogWarning(
                        "El fallback de AI no pudo mejorar la extracción del correo {EmailMessageId}. Motivo: {Reason}",
                        message.Id,
                        aiFallbackError
                    );
                }
            }

            if (
                !response.Success
                || response.Rows.Count == 0
                || response.Summary.TotalRows <= 0
            )
            {
                var reason = BuildFailureReason(
                    response,
                    usedAiFallback,
                    aiExecutionId,
                    aiFallbackError
                );
                job.MarkFailed(response.ExtractionExecutionId, reason);
                message.MarkNeedsReview(reason);
                attachment?.MarkFailed(reason);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            if (attachment is not null)
            {
                attachment.MarkExtracted();
            }

            var shouldSendToPricing =
                account.AutoSendToPricing && confidence >= account.AutoSendMinConfidence;
            if (!shouldSendToPricing)
            {
                var source = usedAiFallback ? " luego del fallback de AI" : string.Empty;
                var reason = confidence <= 0m
                    ? "La extracción no produjo datos confiables para Pricing."
                    : $"Extracción correcta{source} con confianza {confidence:0.##}%. "
                        + "Requiere revisión antes de crear tarifa en Pricing.";

                var catalogMismatchSummary = BuildCatalogMismatchSummary(response);
                if (!string.IsNullOrWhiteSpace(catalogMismatchSummary))
                {
                    reason += $" {catalogMismatchSummary}";
                }

                if (!string.IsNullOrWhiteSpace(aiFallbackError))
                {
                    reason += $" El fallback de AI no pudo completarse: {aiFallbackError}";
                }
                job.MarkNeedsReview(response.ExtractionExecutionId, confidence, reason);
                message.MarkNeedsReview(reason);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var submitResult = await pricingImportClient.SubmitAsync(
                new PricingImportSubmissionRequest(
                    response.ExtractionExecutionId!.Value,
                    response.PricingImportId,
                    message.Id,
                    attachment?.Id,
                    "Email",
                    message.FromAddress,
                    message.Subject,
                    input.OriginalFileName,
                    confidence,
                    response
                )
                {
                    ContentSourceType = usedAiFallback
                        ? $"{job.SourceType}:AI"
                        : job.SourceType.ToString(),
                },
                cancellationToken
            );

            if (!submitResult.Success || !submitResult.PricingImportBatchId.HasValue)
            {
                var reason = submitResult.ErrorMessage
                    ?? "No se pudo crear el lote de Pricing desde la extracción.";
                job.MarkNeedsReview(response.ExtractionExecutionId, confidence, reason);
                message.MarkNeedsReview(reason);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            job.MarkSentToPricing(
                response.ExtractionExecutionId,
                submitResult.PricingImportBatchId.Value,
                confidence
            );
            message.MarkExtracted();
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Correo {EmailMessageId} enviado a Pricing. Fallback AI: {UsedAiFallback}; ejecución AI: {AiExecutionId}; confianza: {Confidence}.",
                message.Id,
                usedAiFallback,
                aiExecutionId,
                confidence
            );
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Falló el trabajo de extracción de correo {EmailExtractionJobId}.",
                job.Id
            );
            job.MarkFailed(null, exception.Message);
            message?.MarkFailed(exception.Message);
            attachment?.MarkFailed(exception.Message);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<AiFallbackAttempt> TryAiFallbackAsync(
        EmailExtractionJob job,
        EmailMessage message,
        EmailExtractionInput input,
        ExtractPricingDataResponse previousResponse,
        decimal previousConfidence,
        CancellationToken cancellationToken
    )
    {
        if (!ReadBoolean(configuration["AI:EmailFallback:Enabled"], true))
        {
            return AiFallbackAttempt.NotApplied(
                previousResponse,
                previousConfidence,
                "El fallback de AI está deshabilitado."
            );
        }

        var sourceContent = await aiEmailContentReader.ReadAsTextAsync(
            input.Request.OriginalFileName,
            input.Request.ContentType,
            input.Request.FileExtension,
            input.Request.FileContent,
            cancellationToken
        );

        var analysis = await aiExtractionClient.AnalyzePricingEmailAsync(
            new AiPricingEmailAnalysisRequest(
                message.Id,
                input.Attachment?.Id,
                message.FromAddress,
                message.Subject,
                message.BodyText,
                message.BodyHtml,
                job.SourceType.ToString(),
                input.OriginalFileName,
                input.Request.ContentType,
                sourceContent,
                input.Request.CorrelationId,
                previousResponse.ErrorCode,
                previousResponse.ErrorMessage,
                previousConfidence
            ),
            cancellationToken
        );

        if (!analysis.Success || analysis.Rows.Count == 0)
        {
            return AiFallbackAttempt.NotApplied(
                previousResponse,
                previousConfidence,
                analysis.ErrorMessage
                    ?? analysis.Warnings.FirstOrDefault()
                    ?? "AI no devolvió filas de tarifas."
            );
        }

        var csvContent = BuildAiNormalizedCsv(analysis.Rows, analysis.Warnings);
        var csvBytes = Encoding.UTF8.GetBytes(csvContent);
        var aiFileName = input.Attachment is null
            ? $"ai-email-body-{message.Id:N}.csv"
            : $"ai-email-attachment-{input.Attachment.Id:N}.csv";

        var aiRequest = new ExtractionDataRequest(
            input.Request.PricingImportId,
            input.Request.CorrelationId,
            aiFileName,
            "text/csv",
            ".csv",
            csvBytes.LongLength,
            FileHashCalculator.ComputeSha256(csvBytes),
            null,
            input.Request.RequestedBy,
            "AI pricing-email-analysis",
            csvBytes
        )
        {
            SourceOriginType = job.SourceType == EmailContentSourceType.Attachment
                ? "EmailAttachmentAiFallback"
                : "EmailBodyAiFallback",
            SourceOriginId = input.Request.SourceOriginId,
            SourceEmailMessageId = message.Id,
            SourceEmailAttachmentId = input.Attachment?.Id,
        };

        var normalizedResponse = await extractionPipeline.ExtractPricingDataAsync(
            aiRequest,
            cancellationToken
        );

        if (!normalizedResponse.Success || normalizedResponse.Rows.Count == 0)
        {
            return AiFallbackAttempt.NotApplied(
                previousResponse,
                previousConfidence,
                normalizedResponse.ErrorMessage
                    ?? "DataExtraction no pudo normalizar la salida estructurada de AI."
            );
        }

        var validatedConfidence = classifier.CalculateExtractionConfidence(
            normalizedResponse,
            message,
            input.Attachment
        );
        var finalConfidence = analysis.Confidence > 0m
            ? Math.Min(analysis.Confidence, validatedConfidence)
            : validatedConfidence;

        logger.LogInformation(
            "Fallback AI aplicado al correo {EmailMessageId}. Ejecución AI {AiExecutionId}; ejecución DataExtraction {ExtractionExecutionId}; filas {Rows}; confianza AI {AiConfidence}; confianza final {FinalConfidence}.",
            message.Id,
            analysis.AiExecutionId,
            normalizedResponse.ExtractionExecutionId,
            normalizedResponse.Rows.Count,
            analysis.Confidence,
            finalConfidence
        );

        return new AiFallbackAttempt(
            true,
            normalizedResponse,
            finalConfidence,
            analysis.AiExecutionId,
            null
        );
    }

    private async Task<EmailExtractionInput> BuildExtractionInputAsync(
        EmailExtractionJob job,
        EmailMessage message,
        CancellationToken cancellationToken
    )
    {
        if (job.SourceType == EmailContentSourceType.Attachment)
        {
            if (!job.EmailAttachmentId.HasValue)
            {
                throw new InvalidOperationException(
                    "El trabajo de adjunto no tiene EmailAttachmentId."
                );
            }

            var attachment = await dbContext.EmailAttachments.FirstOrDefaultAsync(
                x => x.Id == job.EmailAttachmentId.Value && !x.IsDeleted,
                cancellationToken
            );

            if (attachment is null)
            {
                throw new InvalidOperationException(
                    "No se encontró el adjunto asociado al trabajo."
                );
            }

            var attachmentContent = await fileStorage.ReadAsync(
                attachment.StoragePath,
                cancellationToken
            );
            var request = new ExtractionDataRequest(
                job.ProvisionalPricingImportId,
                $"email-{message.Id:N}-{attachment.Id:N}",
                attachment.FileName,
                attachment.ContentType,
                attachment.FileExtension,
                attachment.SizeBytes,
                attachment.FileHash,
                null,
                null,
                "Email Ingestion Worker",
                attachmentContent
            )
            {
                SourceOriginType = "EmailAttachment",
                SourceOriginId = attachment.Id,
                SourceEmailMessageId = message.Id,
                SourceEmailAttachmentId = attachment.Id,
                StoragePath = attachment.StoragePath,
            };

            return new EmailExtractionInput(request, attachment.FileName, attachment);
        }

        var body = !string.IsNullOrWhiteSpace(message.BodyHtml)
            ? message.BodyHtml!
            : message.BodyText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException("El correo no tiene cuerpo para procesar.");
        }

        var extension = !string.IsNullOrWhiteSpace(message.BodyHtml) ? ".html" : ".txt";
        var contentType = !string.IsNullOrWhiteSpace(message.BodyHtml)
            ? "text/html"
            : "text/plain";
        var bodyContent = Encoding.UTF8.GetBytes(body);
        var fileName = $"email-body-{message.Id:N}{extension}";

        var requestBody = new ExtractionDataRequest(
            job.ProvisionalPricingImportId,
            $"email-{message.Id:N}-body",
            fileName,
            contentType,
            extension,
            bodyContent.LongLength,
            FileHashCalculator.ComputeSha256(bodyContent),
            null,
            null,
            "Email Ingestion Worker",
            bodyContent
        )
        {
            SourceOriginType = "EmailBody",
            SourceOriginId = message.Id,
            SourceEmailMessageId = message.Id,
        };

        return new EmailExtractionInput(requestBody, fileName, null);
    }

    private Task<bool> HasProcessableAttachmentAsync(
        Guid emailMessageId,
        CancellationToken cancellationToken
    )
    {
        string[] aiReadableExtensions = [".docx", ".rtf", ".json", ".xml", ".md", ".tsv", ".log"];

        return dbContext.EmailAttachments.AnyAsync(
            attachment =>
                attachment.EmailMessageId == emailMessageId
                && !attachment.IsDeleted
                && attachment.SizeBytes > 0
                && (
                    attachment.SourceFileType == SourceFileType.Excel
                    || attachment.SourceFileType == SourceFileType.Csv
                    || attachment.SourceFileType == SourceFileType.Pdf
                    || attachment.SourceFileType == SourceFileType.Email
                    || (
                        attachment.FileExtension != null
                        && aiReadableExtensions.Contains(attachment.FileExtension)
                    )
                ),
            cancellationToken
        );
    }

    private static bool ShouldUseAiFallback(
        ExtractPricingDataResponse response,
        decimal confidence,
        decimal minimumConfidence
    )
    {
        return !response.Success
            || response.Rows.Count == 0
            || response.Summary.TotalRows == 0
            || confidence < minimumConfidence;
    }

    private static string BuildAiNormalizedCsv(
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

        foreach (var row in rows)
        {
            var remarks = JoinRemarks(row.Remarks, globalWarnings);
            string?[] values =
            [
                row.OriginPort,
                row.PortOfExit,
                row.DestinationPort,
                row.ContainerType,
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

            builder.AppendLine(string.Join(",", values.Select(EscapeCsv)));
        }

        return builder.ToString();
    }

    private static string BuildFailureReason(
        ExtractPricingDataResponse response,
        bool usedAiFallback,
        Guid? aiExecutionId,
        string? aiFallbackError
    )
    {
        var reason = !string.IsNullOrWhiteSpace(response.ErrorMessage)
            ? response.ErrorMessage.Trim()
            : response.Rows.Count == 0 || response.Summary.TotalRows <= 0
                ? "DataExtraction no encontró filas de tarifas en el correo."
                : "La extracción del correo falló.";

        if (usedAiFallback)
        {
            return $"{reason} Fallback AI aplicado (ejecución {aiExecutionId?.ToString() ?? "sin id"}), "
                + "pero la salida no superó la validación final de DataExtraction.";
        }

        if (!string.IsNullOrWhiteSpace(aiFallbackError))
        {
            return $"{reason} El fallback de AI tampoco pudo completar la extracción: "
                + aiFallbackError.Trim();
        }

        return reason;
    }

    private static string? BuildCatalogMismatchSummary(
        ExtractPricingDataResponse response
    )
    {
        var mismatches = response.Issues
            .Where(issue =>
                issue.IsBlocking
                && issue.Code.StartsWith("unknown_", StringComparison.OrdinalIgnoreCase)
            )
            .Select(issue => string.IsNullOrWhiteSpace(issue.RawValue)
                ? issue.ColumnName ?? issue.Code
                : $"{issue.ColumnName ?? issue.Code}='{issue.RawValue}'")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        return mismatches.Length == 0
            ? null
            : "No coincidieron con Config: " + string.Join(", ", mismatches) + ".";
    }

    private static string EscapeCsv(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return escaped.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0
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

    private static bool ReadBoolean(string? value, bool fallback)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private sealed record EmailExtractionInput(
        ExtractionDataRequest Request,
        string OriginalFileName,
        EmailAttachment? Attachment
    );

    private sealed record AiFallbackAttempt(
        bool Applied,
        ExtractPricingDataResponse Response,
        decimal Confidence,
        Guid? AiExecutionId,
        string? ErrorMessage
    )
    {
        public static AiFallbackAttempt NotApplied(
            ExtractPricingDataResponse response,
            decimal confidence,
            string errorMessage
        ) => new(false, response, confidence, null, errorMessage);
    }
}
