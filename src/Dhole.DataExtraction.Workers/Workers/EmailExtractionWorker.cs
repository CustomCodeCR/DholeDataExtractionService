using CustomCodeFramework.Workers.Abstractions;
using Dhole.DataExtraction.Application.Abstractions.Emails;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
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
                && await HasSupportedAttachmentAsync(message.Id, cancellationToken)
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

            var response = await extractionPipeline.ExtractPricingDataAsync(input.Request, cancellationToken);
            var confidence = classifier.CalculateExtractionConfidence(response, message, attachment);

            if (!response.Success)
            {
                job.MarkFailed(response.ExtractionExecutionId, response.ErrorMessage ?? "La extracción del correo falló.");
                message.MarkNeedsReview(response.ErrorMessage ?? "La extracción del correo falló.");
                attachment?.MarkFailed(response.ErrorMessage ?? "La extracción del adjunto falló.");
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            if (attachment is not null)
            {
                attachment.MarkExtracted();
            }

            var shouldSendToPricing = account.AutoSendToPricing && confidence >= account.AutoSendMinConfidence;
            if (!shouldSendToPricing)
            {
                var reason = $"Extracción correcta con confianza {confidence:0.##}%. Requiere revisión antes de crear tarifa en Pricing.";
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
                    ContentSourceType = job.SourceType.ToString(),
                },
                cancellationToken
            );

            if (!submitResult.Success || !submitResult.PricingImportBatchId.HasValue)
            {
                var reason = submitResult.ErrorMessage ?? "No se pudo crear el lote de Pricing desde la extracción.";
                job.MarkNeedsReview(response.ExtractionExecutionId, confidence, reason);
                message.MarkNeedsReview(reason);
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            job.MarkSentToPricing(response.ExtractionExecutionId, submitResult.PricingImportBatchId.Value, confidence);
            message.MarkExtracted();
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Falló el trabajo de extracción de correo {EmailExtractionJobId}.", job.Id);
            job.MarkFailed(null, exception.Message);
            message?.MarkFailed(exception.Message);
            attachment?.MarkFailed(exception.Message);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
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
                throw new InvalidOperationException("El trabajo de adjunto no tiene EmailAttachmentId.");
            }

            var attachment = await dbContext.EmailAttachments.FirstOrDefaultAsync(
                x => x.Id == job.EmailAttachmentId.Value && !x.IsDeleted,
                cancellationToken
            );

            if (attachment is null)
            {
                throw new InvalidOperationException("No se encontró el adjunto asociado al trabajo.");
            }

            var attachmentContent = await fileStorage.ReadAsync(attachment.StoragePath, cancellationToken);
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

        var body = !string.IsNullOrWhiteSpace(message.BodyHtml) ? message.BodyHtml! : message.BodyText ?? string.Empty;
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException("El correo no tiene cuerpo para procesar.");
        }

        var extension = !string.IsNullOrWhiteSpace(message.BodyHtml) ? ".html" : ".txt";
        var contentType = !string.IsNullOrWhiteSpace(message.BodyHtml) ? "text/html" : "text/plain";
        var bodyContent = System.Text.Encoding.UTF8.GetBytes(body);
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

    private Task<bool> HasSupportedAttachmentAsync(
        Guid emailMessageId,
        CancellationToken cancellationToken
    )
    {
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
                ),
            cancellationToken
        );
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
