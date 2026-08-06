using System.Text;
using CustomCodeFramework.Workers.Abstractions;
using Dhole.DataExtraction.Application.Abstractions.Emails;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Contracts.Extraction;
using Dhole.DataExtraction.Domain.Emails;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using Dhole.DataExtraction.Infrastructure.Email;
using Dhole.DataExtraction.Infrastructure.Files;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Workers.Workers;

internal sealed class LegacyEmailExtractionWorker(
    ServiceDbContext dbContext,
    IEmailFileStorage fileStorage,
    IAutomatedPricingExtractionService automatedExtraction,
    IEmailRateClassifier classifier,
    IPricingImportClient pricingImportClient,
    IConfiguration configuration,
    ILogger<LegacyEmailExtractionWorker> logger
) : IBackgroundWorker
{
    public string Name => "data-extraction.email-extraction-legacy";

    public async Task ExecuteAsync(IWorkerExecutionContext context, CancellationToken cancellationToken)
    {
        if (!bool.TryParse(configuration["EmailIngestion:Enabled"], out var enabled) || !enabled)
        {
            logger.LogDebug(
                "{WorkerName} está desactivado por EmailIngestion:Enabled=false.",
                Name
            );
            return;
        }

        await RecoverStaleJobsAsync(cancellationToken);
        await RecoverRedundantBodyJobsAsync(cancellationToken);

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

    private async Task RecoverStaleJobsAsync(CancellationToken cancellationToken)
    {
        var leaseMinutes = ReadPositiveInt(
            configuration["EmailIngestion:ProcessingLeaseMinutes"],
            10
        );
        var cutoff = DateTime.UtcNow.AddMinutes(-leaseMinutes);
        var staleJobs = await dbContext.EmailExtractionJobs
            .Where(job =>
                job.Status == EmailExtractionJobStatus.Extracting
                && !job.IsDeleted
                && job.StartedAt.HasValue
                && job.StartedAt.Value < cutoff
            )
            .OrderBy(job => job.StartedAt)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (staleJobs.Count == 0)
        {
            return;
        }

        foreach (var staleJob in staleJobs)
        {
            staleJob.Retry();
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogWarning(
            "Se recuperaron {JobCount} trabajos de correo bloqueados en Processing por más de {LeaseMinutes} minutos.",
            staleJobs.Count,
            leaseMinutes
        );
    }

    private async Task RecoverRedundantBodyJobsAsync(
        CancellationToken cancellationToken
    )
    {
        var recoverableStatuses = new[]
        {
            EmailExtractionJobStatus.Pending,
            EmailExtractionJobStatus.NeedsReview,
            EmailExtractionJobStatus.Failed,
        };
        var candidates = await dbContext.EmailExtractionJobs
            .Where(job =>
                !job.IsDeleted
                && job.SourceType == EmailContentSourceType.Body
                && recoverableStatuses.Contains(job.Status)
            )
            .OrderBy(job => job.CreatedAtUtc)
            .Take(250)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return;
        }

        var ignoredCount = 0;
        foreach (var group in candidates.GroupBy(job => job.EmailMessageId))
        {
            var message = await dbContext.EmailMessages.FirstOrDefaultAsync(
                item => item.Id == group.Key && !item.IsDeleted,
                cancellationToken
            );
            if (message is null)
            {
                continue;
            }

            var account = await dbContext.EmailIngestionAccounts.FirstOrDefaultAsync(
                item => item.Id == message.EmailIngestionAccountId && !item.IsDeleted,
                cancellationToken
            );
            if (account is null)
            {
                continue;
            }

            var attachments = await dbContext.EmailAttachments
                .Where(item => item.EmailMessageId == message.Id && !item.IsDeleted)
                .ToListAsync(cancellationToken);
            var classification = classifier.Classify(message, attachments, account);
            if (classification.AttachmentIdsToProcess.Count == 0 || classification.ProcessBody)
            {
                continue;
            }

            foreach (var job in group)
            {
                job.MarkIgnored(
                    "Se archivó el resultado del cuerpo porque el correo contiene un adjunto tarifario soportado y el mensaje actual no incluye una tarifa independiente. El historial citado no se procesa como una importación adicional."
                );
                ignoredCount++;
            }

            await EmailJobStateCoordinator.RecalculateAsync(
                dbContext,
                message.Id,
                cancellationToken
            );
        }

        if (ignoredCount == 0)
        {
            return;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Se archivaron {JobCount} trabajos redundantes del cuerpo de correos con adjuntos tarifarios.",
            ignoredCount
        );
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
                job.SourceType == EmailContentSourceType.Attachment
                && job.EmailAttachmentId.HasValue
            )
            {
                var candidateAttachment = await dbContext.EmailAttachments.FirstOrDefaultAsync(
                    x => x.Id == job.EmailAttachmentId.Value && !x.IsDeleted,
                    cancellationToken
                );
                if (
                    candidateAttachment is not null
                    && !EmailAttachmentExtractionPolicy.IsSupported(candidateAttachment)
                )
                {
                    job.MarkIgnored(
                        $"El adjunto '{candidateAttachment.FileName}' no se extrajo. Solo se permiten {EmailAttachmentExtractionPolicy.SupportedTypesDescription}."
                    );
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return;
                }
            }

            if (job.SourceType == EmailContentSourceType.Body)
            {
                var processableAttachments = await GetProcessableAttachmentsAsync(
                    message.Id,
                    cancellationToken
                );

                if (processableAttachments.Count > 0)
                {
                    var currentClassification = classifier.Classify(
                        message,
                        processableAttachments,
                        account
                    );

                    if (!currentClassification.ProcessBody)
                    {
                        job.MarkIgnored(
                            "Se omitió el cuerpo porque el correo contiene un adjunto tarifario soportado y la sección actual del mensaje no contiene una tarifa independiente con montos."
                        );
                        await dbContext.SaveChangesAsync(cancellationToken);
                        return;
                    }
                }
            }

            var input = await BuildExtractionInputAsync(job, message, cancellationToken);
            attachment = input.Attachment;
            message.MarkProcessing();

            var automaticResult = await automatedExtraction.ExtractAsync(
                input.Request,
                new AutomatedPricingExtractionContext(
                    message.Id,
                    input.Attachment?.Id,
                    message.FromAddress,
                    message.Subject,
                    message.BodyText,
                    message.BodyHtml,
                    job.SourceType.ToString(),
                    ForceAiAnalysis: false
                ),
                cancellationToken
            );
            var response = automaticResult.Response;
            var confidence = classifier.CalculateExtractionConfidence(
                response,
                message,
                attachment
            );
            if (
                automaticResult.AiApplied
                && automaticResult.AiConfidence is > 0m
            )
            {
                confidence = Math.Min(
                    confidence,
                    automaticResult.AiConfidence.Value
                );
            }
            var usedAiFallback = automaticResult.AiApplied;
            var aiExecutionId = automaticResult.AiExecutionId;
            var aiFallbackError = automaticResult.AiErrorMessage;

            if (automaticResult.AiAttempted && !usedAiFallback && aiFallbackError is not null)
            {
                logger.LogWarning(
                    "AI no pudo mejorar la extracción del correo {EmailMessageId}. Motivo: {Reason}",
                    message.Id,
                    aiFallbackError
                );
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
                "Correo {EmailMessageId} enviado a Pricing. AI intentada: {AiAttempted}; "
                    + "AI seleccionada: {UsedAiFallback}; ejecución AI: {AiExecutionId}; "
                    + "confianza: {Confidence}.",
                message.Id,
                automaticResult.AiAttempted,
                usedAiFallback,
                aiExecutionId,
                confidence
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Falló el trabajo de extracción de correo {EmailExtractionJobId}.",
                job.Id
            );

            await PersistFailureWithoutTrackedExtractionAsync(
                job.Id,
                job.EmailMessageId,
                job.EmailAttachmentId,
                exception
            );
        }
    }

    private async Task PersistFailureWithoutTrackedExtractionAsync(
        Guid jobId,
        Guid emailMessageId,
        Guid? emailAttachmentId,
        Exception originalException
    )
    {
        try
        {
            // El pipeline puede haber agregado ExtractionExecution, SourceDocument y
            // PricingExtractionRecord al contexto antes de que SaveChanges falle.
            // Si se vuelve a guardar el mismo contexto, EF reintenta esas inserciones
            // inválidas y la excepción sale del BackgroundService, deteniendo el host.
            dbContext.ChangeTracker.Clear();

            using var persistenceTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var token = persistenceTimeout.Token;
            var errorMessage = BuildPersistenceErrorMessage(originalException);

            var failedJob = await dbContext.EmailExtractionJobs.FirstOrDefaultAsync(
                x => x.Id == jobId && !x.IsDeleted,
                token
            );
            var failedMessage = await dbContext.EmailMessages.FirstOrDefaultAsync(
                x => x.Id == emailMessageId && !x.IsDeleted,
                token
            );
            EmailAttachment? failedAttachment = null;

            if (emailAttachmentId.HasValue)
            {
                failedAttachment = await dbContext.EmailAttachments.FirstOrDefaultAsync(
                    x => x.Id == emailAttachmentId.Value && !x.IsDeleted,
                    token
                );
            }

            failedJob?.MarkFailed(null, errorMessage);
            failedMessage?.MarkFailed(errorMessage);
            failedAttachment?.MarkFailed(errorMessage);

            await dbContext.SaveChangesAsync(token);
        }
        catch (Exception persistenceException)
        {
            dbContext.ChangeTracker.Clear();
            logger.LogCritical(
                persistenceException,
                "No se pudo persistir el estado fallido del trabajo {EmailExtractionJobId}. El worker continuará activo.",
                jobId
            );
        }
    }

    private static string BuildPersistenceErrorMessage(Exception exception)
    {
        var rootMessage = exception.GetBaseException().Message;
        var message = $"Falló la persistencia de la extracción: {rootMessage}";
        return message.Length <= 4000 ? message : message[..4000];
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
                SourceEmailSubject = message.Subject,
                SourceEmailBodyText = message.BodyText,
                SourceEmailBodyHtml = message.BodyHtml,
                StoragePath = attachment.StoragePath,
            };

            return new EmailExtractionInput(request, attachment.FileName, attachment);
        }

        var body = EmailPricingContentSelector.SelectPreferredBody(
            message.BodyText,
            message.BodyHtml
        );
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException("El correo no tiene cuerpo para procesar.");
        }

        const string extension = ".txt";
        const string contentType = "text/plain";
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
            SourceEmailSubject = message.Subject,
            SourceEmailBodyText = message.BodyText,
            SourceEmailBodyHtml = message.BodyHtml,
        };

        return new EmailExtractionInput(requestBody, fileName, null);
    }

    private async Task<IReadOnlyCollection<EmailAttachment>> GetProcessableAttachmentsAsync(
        Guid emailMessageId,
        CancellationToken cancellationToken
    )
    {
        return await dbContext.EmailAttachments
            .Where(attachment =>
                attachment.EmailMessageId == emailMessageId
                && !attachment.IsDeleted
                && attachment.SizeBytes > 0
                && attachment.FileExtension != null
                && (
                    (attachment.SourceFileType == SourceFileType.Pdf
                        && attachment.FileExtension.ToLower() == ".pdf")
                    || (attachment.SourceFileType == SourceFileType.Csv
                        && attachment.FileExtension.ToLower() == ".csv")
                    || (attachment.SourceFileType == SourceFileType.Excel
                        && attachment.FileExtension.ToLower() == ".xlsx")
                )
            )
            .OrderBy(attachment => attachment.CreatedAtUtc)
            .ToListAsync(cancellationToken);
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

        if (
            !string.IsNullOrWhiteSpace(aiFallbackError)
            && !reason.Contains(aiFallbackError.Trim(), StringComparison.OrdinalIgnoreCase)
        )
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
                issue.Code.StartsWith("unknown_", StringComparison.OrdinalIgnoreCase)
            )
            .Select(issue => string.IsNullOrWhiteSpace(issue.RawValue)
                ? issue.ColumnName ?? issue.Code
                : $"{issue.ColumnName ?? issue.Code}='{issue.RawValue}'")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToArray();

        return mismatches.Length == 0
            ? null
            : "No coincidieron con Config y se conservaron como valores detectados: "
                + string.Join(", ", mismatches)
                + ".";
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

}
