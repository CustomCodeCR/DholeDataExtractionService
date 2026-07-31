using System.Data;
using Dhole.DataExtraction.Api.Extensions;
using Dhole.DataExtraction.Application.Abstractions.Emails;
using Dhole.DataExtraction.Application.Abstractions.Messaging;
using Dhole.DataExtraction.Contracts.AsyncEmail;
using Dhole.DataExtraction.Contracts.Extraction;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Api.Endpoints.Emails;

public static class EmailIngestionEndpoints
{
    public static IEndpointRouteBuilder MapEmailIngestionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/data-extraction/email")
            .WithTags("Email Ingestion")
            .RequireAuthorization();

        group.MapGet("/accounts", async (
            int? pageNumber,
            int? pageSize,
            string? search,
            bool? isActive,
            ServiceDbContext dbContext,
            CancellationToken cancellationToken
        ) =>
        {
            var page = Math.Max(pageNumber ?? 1, 1);
            var size = Math.Clamp(pageSize ?? 20, 1, 100);
            var query = dbContext.EmailIngestionAccounts.AsNoTracking().Where(x => !x.IsDeleted);

            if (isActive.HasValue)
            {
                query = query.Where(x => x.IsActive == isActive.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var value = search.Trim().ToLowerInvariant();
                query = query.Where(x =>
                    x.Name.ToLower().Contains(value)
                    || x.EmailAddress.ToLower().Contains(value)
                    || x.Username.ToLower().Contains(value)
                    || x.Host.ToLower().Contains(value)
                );
            }

            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .OrderByDescending(x => x.CreatedAtUtc)
                .Skip((page - 1) * size)
                .Take(size)
                .Select(x => new EmailAccountResponse(
                    x.Id,
                    x.Name,
                    x.EmailAddress,
                    x.ProviderType.ToString(),
                    x.Host,
                    x.Port,
                    x.UseSsl,
                    x.Username,
                    x.SecretReference,
                    x.FolderName,
                    x.PollingIntervalMinutes,
                    x.AutoProcess,
                    x.AutoSendToPricing,
                    x.AutoSendMinConfidence,
                    x.ProcessBodyWhenNoSupportedAttachments,
                    x.ProcessBodyEvenWithAttachments,
                    x.AllowedSenders,
                    x.IsActive,
                    x.LastProcessedUid,
                    x.LastSyncAt,
                    x.LastSyncError
                ))
                .ToListAsync(cancellationToken);

            return Results.Ok(new { pageNumber = page, pageSize = size, total, items });
        });

        group.MapPost("/accounts", async (
            UpsertEmailAccountRequest request,
            ServiceDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken
        ) =>
        {
            if (await dbContext.EmailIngestionAccounts.AnyAsync(x => x.EmailAddress == request.EmailAddress.Trim().ToLower() && !x.IsDeleted, cancellationToken))
            {
                return Results.Conflict(new { code = "DataExtraction.EmailAccountDuplicated", message = "Ya existe una cuenta configurada con ese correo." });
            }

            var account = EmailIngestionAccount.Create(
                request.Name,
                request.EmailAddress,
                request.ProviderType,
                request.Host,
                request.Port,
                request.UseSsl,
                request.Username,
                request.SecretReference,
                request.FolderName,
                request.PollingIntervalMinutes,
                request.AutoProcess,
                request.AutoSendToPricing,
                request.AutoSendMinConfidence,
                request.ProcessBodyWhenNoSupportedAttachments,
                request.ProcessBodyEvenWithAttachments,
                request.AllowedSenders,
                httpContext.GetCurrentUserId()
            );

            dbContext.EmailIngestionAccounts.Add(account);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Created($"/api/data-extraction/email/accounts/{account.Id}", new { account.Id });
        });

        group.MapPut("/accounts/{id:guid}", async (
            Guid id,
            UpsertEmailAccountRequest request,
            ServiceDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken
        ) =>
        {
            var account = await dbContext.EmailIngestionAccounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
            if (account is null)
            {
                return Results.NotFound(new { code = "DataExtraction.EmailAccountNotFound", message = "No se encontró la cuenta de correo." });
            }

            var email = request.EmailAddress.Trim().ToLowerInvariant();
            if (await dbContext.EmailIngestionAccounts.AnyAsync(x => x.EmailAddress == email && x.Id != id && !x.IsDeleted, cancellationToken))
            {
                return Results.Conflict(new { code = "DataExtraction.EmailAccountDuplicated", message = "Ya existe otra cuenta con ese correo." });
            }

            account.Update(
                request.Name,
                request.EmailAddress,
                request.ProviderType,
                request.Host,
                request.Port,
                request.UseSsl,
                request.Username,
                request.SecretReference,
                request.FolderName,
                request.PollingIntervalMinutes,
                request.AutoProcess,
                request.AutoSendToPricing,
                request.AutoSendMinConfidence,
                request.ProcessBodyWhenNoSupportedAttachments,
                request.ProcessBodyEvenWithAttachments,
                request.AllowedSenders,
                httpContext.GetCurrentUserId()
            );

            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { account.Id });
        });

        group.MapPatch("/accounts/{id:guid}/active", async (
            Guid id,
            SetEmailAccountActiveRequest request,
            ServiceDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken
        ) =>
        {
            var account = await dbContext.EmailIngestionAccounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
            if (account is null)
            {
                return Results.NotFound(new { code = "DataExtraction.EmailAccountNotFound", message = "No se encontró la cuenta de correo." });
            }

            account.SetActive(request.IsActive, httpContext.GetCurrentUserId());
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { account.Id, account.IsActive });
        });

        group.MapDelete("/accounts/{id:guid}", async (
            Guid id,
            ServiceDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken
        ) =>
        {
            var account = await dbContext.EmailIngestionAccounts.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
            if (account is null)
            {
                return Results.NotFound(new { code = "DataExtraction.EmailAccountNotFound", message = "No se encontró la cuenta de correo." });
            }

            account.Delete(httpContext.GetCurrentUserId());
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });

        group.MapGet("/messages", async (
            int? pageNumber,
            int? pageSize,
            string? search,
            EmailMessageStatus? status,
            Guid? accountId,
            ServiceDbContext dbContext,
            CancellationToken cancellationToken
        ) =>
        {
            var page = Math.Max(pageNumber ?? 1, 1);
            var size = Math.Clamp(pageSize ?? 20, 1, 100);
            var query = dbContext.EmailMessages.AsNoTracking().Where(x => !x.IsDeleted);

            if (accountId.HasValue)
            {
                query = query.Where(x => x.EmailIngestionAccountId == accountId.Value);
            }

            if (status.HasValue)
            {
                query = query.Where(x => x.Status == status.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var value = search.Trim().ToLowerInvariant();
                query = query.Where(x =>
                    x.Subject.ToLower().Contains(value)
                    || x.FromAddress.ToLower().Contains(value)
                    || (x.FromName != null && x.FromName.ToLower().Contains(value))
                );
            }

            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .OrderByDescending(x => x.ReceivedAt)
                .Skip((page - 1) * size)
                .Take(size)
                .Select(x => new EmailMessageListResponse(
                    x.Id,
                    x.EmailIngestionAccountId,
                    x.FromName,
                    x.FromAddress,
                    x.Subject,
                    x.ReceivedAt,
                    x.HasAttachments,
                    x.Status.ToString(),
                    x.ClassificationConfidence,
                    x.ClassificationReason,
                    x.ErrorMessage
                ))
                .ToListAsync(cancellationToken);

            return Results.Ok(new { pageNumber = page, pageSize = size, total, items });
        });

        group.MapGet("/messages/{id:guid}", async (
            Guid id,
            ServiceDbContext dbContext,
            CancellationToken cancellationToken
        ) =>
        {
            var message = await dbContext.EmailMessages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
            if (message is null)
            {
                return Results.NotFound(new { code = "DataExtraction.EmailMessageNotFound", message = "No se encontró el correo." });
            }

            var attachments = await dbContext.EmailAttachments.AsNoTracking()
                .Where(x => x.EmailMessageId == id && !x.IsDeleted)
                .OrderBy(x => x.FileName)
                .Select(x => new EmailAttachmentResponse(
                    x.Id,
                    x.FileName,
                    x.ContentType,
                    x.FileExtension,
                    x.SizeBytes,
                    x.FileHash,
                    x.SourceFileType.ToString(),
                    x.Status.ToString(),
                    x.ErrorMessage,
                    x.StoragePath
                ))
                .ToListAsync(cancellationToken);

            var jobs = await dbContext.EmailExtractionJobs.AsNoTracking()
                .Where(x => x.EmailMessageId == id && !x.IsDeleted)
                .OrderByDescending(x => x.CreatedAtUtc)
                .Select(x => new EmailExtractionJobResponse(
                    x.Id,
                    x.EmailMessageId,
                    x.EmailAttachmentId,
                    x.SourceType.ToString(),
                    x.ProvisionalPricingImportId,
                    x.ExtractionExecutionId,
                    x.PricingImportBatchId,
                    x.AiRequestId,
                    x.AiExecutionId,
                    x.PricingRequestId,
                    x.Status.ToString(),
                    x.ConfidenceScore,
                    x.AttemptCount,
                    x.NextAttemptAtUtc,
                    x.LeaseExpiresAtUtc,
                    x.LastHeartbeatAtUtc,
                    x.LastErrorCode,
                    x.ErrorMessage,
                    x.StartedAt,
                    x.FinishedAt
                ))
                .ToListAsync(cancellationToken);

            return Results.Ok(new EmailMessageDetailResponse(
                message.Id,
                message.EmailIngestionAccountId,
                message.ExternalMessageId,
                message.Uid,
                message.MessageIdHeader,
                message.FromName,
                message.FromAddress,
                message.ToAddresses,
                message.CcAddresses,
                message.Subject,
                message.BodyText,
                message.BodyHtml,
                message.ReceivedAt,
                message.HasAttachments,
                message.RawEmailStoragePath,
                message.Status.ToString(),
                message.ClassificationConfidence,
                message.ClassificationReason,
                message.ErrorMessage,
                attachments,
                jobs
            ));
        });

        group.MapPost("/messages/{id:guid}/ignore", async (
            Guid id,
            IgnoreEmailMessageRequest request,
            ServiceDbContext dbContext,
            HttpContext httpContext,
            CancellationToken cancellationToken
        ) =>
        {
            var message = await dbContext.EmailMessages.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
            if (message is null)
            {
                return Results.NotFound(new { code = "DataExtraction.EmailMessageNotFound", message = "No se encontró el correo." });
            }

            message.MarkIgnored(request.Reason, httpContext.GetCurrentUserId());
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Ok(new { message.Id, status = message.Status.ToString() });
        });

        group.MapPost("/messages/{id:guid}/reprocess", async (
            Guid id,
            ServiceDbContext dbContext,
            IEmailRateClassifier classifier,
            HttpContext httpContext,
            CancellationToken cancellationToken
        ) =>
        {
            var message = await dbContext.EmailMessages.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
            if (message is null)
            {
                return Results.NotFound(new { code = "DataExtraction.EmailMessageNotFound", message = "No se encontró el correo." });
            }

            var attachments = await dbContext.EmailAttachments
                .Where(x => x.EmailMessageId == id && !x.IsDeleted)
                .ToListAsync(cancellationToken);
            var account = await dbContext.EmailIngestionAccounts.FirstOrDefaultAsync(
                x => x.Id == message.EmailIngestionAccountId && !x.IsDeleted,
                cancellationToken
            );

            if (account is null)
            {
                return Results.BadRequest(new
                {
                    code = "DataExtraction.EmailAccountNotFound",
                    message = "No se encontró la cuenta de correo asociada al mensaje.",
                });
            }

            var classification = classifier.Classify(message, attachments, account);
            if (!classification.ContainsRates)
            {
                message.MarkIgnored(classification.Reason, httpContext.GetCurrentUserId());
                await dbContext.SaveChangesAsync(cancellationToken);
                return Results.BadRequest(new
                {
                    code = "DataExtraction.EmailHasNoProcessableContent",
                    message = classification.Reason,
                });
            }

            var requestedBy = httpContext.GetCurrentUserId();
            var existingJobs = await dbContext.EmailExtractionJobs
                .Where(job => job.EmailMessageId == message.Id && !job.IsDeleted)
                .OrderByDescending(job => job.CreatedAtUtc)
                .ToListAsync(cancellationToken);
            var queuedJobs = 0;

            foreach (var attachmentId in classification.AttachmentIdsToProcess)
            {
                var existingJob = existingJobs.FirstOrDefault(job =>
                    job.SourceType == EmailContentSourceType.Attachment
                    && job.EmailAttachmentId == attachmentId
                );

                if (existingJob is null)
                {
                    dbContext.EmailExtractionJobs.Add(
                        EmailExtractionJob.CreateAttachmentJob(
                            message.Id,
                            attachmentId,
                            requestedBy
                        )
                    );
                    queuedJobs++;
                }
                else if (
                    existingJob.Status
                    is EmailExtractionJobStatus.NeedsReview
                        or EmailExtractionJobStatus.Failed
                        or EmailExtractionJobStatus.Ignored
                )
                {
                    existingJob.Retry(requestedBy);
                    queuedJobs++;
                }
            }

            if (classification.ProcessBody)
            {
                var existingBodyJob = existingJobs.FirstOrDefault(job =>
                    job.SourceType == EmailContentSourceType.Body
                );

                if (existingBodyJob is null)
                {
                    dbContext.EmailExtractionJobs.Add(
                        EmailExtractionJob.CreateBodyJob(message.Id, requestedBy)
                    );
                    queuedJobs++;
                }
                else if (
                    existingBodyJob.Status
                    is EmailExtractionJobStatus.NeedsReview
                        or EmailExtractionJobStatus.Failed
                        or EmailExtractionJobStatus.Ignored
                )
                {
                    existingBodyJob.Retry(requestedBy);
                    queuedJobs++;
                }
            }

            if (queuedJobs == 0)
            {
                return Results.Accepted(
                    $"/api/data-extraction/email/messages/{message.Id}",
                    new
                    {
                        message.Id,
                        queuedJobs,
                        message = "El contenido ya está procesándose o fue enviado a Pricing.",
                    }
                );
            }

            message.MarkQueued(
                classification.ConfidenceScore,
                $"Reprocesamiento solicitado manualmente. {classification.Reason}",
                requestedBy
            );
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Accepted(
                $"/api/data-extraction/email/messages/{message.Id}",
                new { message.Id, queuedJobs }
            );
        });

        group.MapPost("/extraction-jobs/{jobId:guid}/send-to-pricing", async (
            Guid jobId,
            ServiceDbContext dbContext,
            IIntegrationEventOutboxWriter outbox,
            HttpContext httpContext,
            CancellationToken cancellationToken
        ) =>
        {
            var job = await dbContext.EmailExtractionJobs.FirstOrDefaultAsync(
                item => item.Id == jobId && !item.IsDeleted,
                cancellationToken
            );
            if (job is null)
            {
                return Results.NotFound(new
                {
                    code = "DataExtraction.EmailExtractionJobNotFound",
                    message = "No se encontró el trabajo de extracción.",
                });
            }

            if (job.Status == EmailExtractionJobStatus.SentToPricing && job.PricingImportBatchId.HasValue)
            {
                return Results.Ok(new
                {
                    job.Id,
                    status = job.Status.ToString(),
                    job.PricingImportBatchId,
                });
            }

            if (job.Status == EmailExtractionJobStatus.AwaitingPricing)
            {
                return Results.Accepted(
                    $"/api/data-extraction/email/messages/{job.EmailMessageId}",
                    new
                    {
                        job.Id,
                        status = job.Status.ToString(),
                        job.PricingRequestId,
                    }
                );
            }

            if (job.Status != EmailExtractionJobStatus.NeedsReview)
            {
                return Results.Conflict(new
                {
                    code = "DataExtraction.EmailExtractionNotReviewable",
                    message = "Solo una extracción en estado Necesita revisión puede enviarse manualmente a Pricing.",
                });
            }

            if (!job.ExtractionExecutionId.HasValue)
            {
                return Results.BadRequest(new
                {
                    code = "DataExtraction.MissingExtractionExecutionId",
                    message = "La extracción no tiene una ejecución persistida para revisar.",
                });
            }

            var message = await dbContext.EmailMessages.FirstOrDefaultAsync(
                item => item.Id == job.EmailMessageId && !item.IsDeleted,
                cancellationToken
            );
            if (message is null)
            {
                return Results.NotFound(new
                {
                    code = "DataExtraction.EmailMessageNotFound",
                    message = "No se encontró el correo asociado a la extracción.",
                });
            }

            var executionId = job.ExtractionExecutionId.Value;
            var execution = await dbContext.ExtractionExecutions.AsNoTracking().FirstOrDefaultAsync(
                item => item.Id == executionId && !item.IsDeleted,
                cancellationToken
            );
            var sourceDocument = await dbContext.SourceDocuments.AsNoTracking().FirstOrDefaultAsync(
                item => item.ExtractionExecutionId == executionId && !item.IsDeleted,
                cancellationToken
            );
            if (execution is null || sourceDocument is null)
            {
                return Results.BadRequest(new
                {
                    code = "DataExtraction.ExtractionResultNotFound",
                    message = "No se encontró el resultado determinístico persistido para crear la revisión.",
                });
            }

            var extractedRecords = await dbContext.PricingExtractionRecords.AsNoTracking()
                .Where(item => item.ExtractionExecutionId == executionId && !item.IsDeleted)
                .OrderBy(item => item.SourceRowNumber)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken);
            if (extractedRecords.Count == 0)
            {
                return Results.BadRequest(new
                {
                    code = "DataExtraction.NoExtractedRows",
                    message = "La extracción no contiene filas que puedan enviarse a revisión.",
                });
            }

            var extractionIssues = await dbContext.ExtractionIssues.AsNoTracking()
                .Where(item => item.ExtractionExecutionId == executionId && !item.IsDeleted)
                .OrderBy(item => item.SourceRowNumber)
                .ThenBy(item => item.Id)
                .ToListAsync(cancellationToken);
            var response = new ExtractPricingDataResponse(
                true,
                execution.Id,
                job.ProvisionalPricingImportId,
                execution.CorrelationId,
                new ExtractionSummaryDto(
                    execution.TotalRows,
                    execution.ValidRows,
                    execution.WarningRows,
                    execution.InvalidRows,
                    extractionIssues.Count > 0
                ),
                new ExtractionSourceDocumentDto(
                    sourceDocument.Id,
                    sourceDocument.ExtractionExecutionId,
                    sourceDocument.OriginalFileName,
                    sourceDocument.ContentType,
                    sourceDocument.FileExtension,
                    sourceDocument.FileSizeBytes,
                    sourceDocument.FileHash,
                    sourceDocument.SourceFileType.ToString(),
                    sourceDocument.StoragePath
                ),
                extractedRecords.Select(ToExtractedPricingRowDto).ToList(),
                extractionIssues.Select(issue => new ExtractionIssueDto(
                    issue.Id,
                    issue.ExtractionExecutionId,
                    issue.PricingExtractionRecordId,
                    issue.Code,
                    issue.Message,
                    issue.IsBlocking,
                    issue.SourceSheetName,
                    issue.SourceRowNumber,
                    issue.ColumnName,
                    issue.RawValue
                )).ToList(),
                null,
                null
            );

            var requestId = Guid.NewGuid();
            var correlationId = string.IsNullOrWhiteSpace(execution.CorrelationId)
                ? Guid.NewGuid().ToString("N")
                : execution.CorrelationId;
            var pricingEvent = new PricingImportFromExtractionRequestedIntegrationEvent(
                Guid.NewGuid(),
                requestId,
                job.Id,
                execution.Id,
                job.ProvisionalPricingImportId,
                message.Id,
                job.EmailAttachmentId,
                "Email",
                message.FromAddress,
                message.Subject,
                sourceDocument.OriginalFileName,
                job.ConfidenceScore ?? message.ClassificationConfidence ?? 0m,
                $"{job.SourceType}:ManualReview",
                correlationId,
                response,
                DateTime.UtcNow
            );
            var requestedBy = httpContext.GetCurrentUserId();

            await dbContext.ExecuteInRetryableTransactionAsync(
                async () =>
                {
                    job.MarkAwaitingPricingForManualReview(
                        requestId,
                        execution.Id,
                        requestedBy
                    );
                    message.MarkProcessing(requestedBy);
                    await outbox.WriteAsync(
                        typeof(PricingImportFromExtractionRequestedIntegrationEvent).FullName!,
                        AsyncEmailMessageTypes.PricingRequested,
                        pricingEvent,
                        correlationId,
                        cancellationToken
                    );
                    await dbContext.SaveChangesAsync(cancellationToken);
                },
                IsolationLevel.ReadCommitted,
                cancellationToken
            );

            return Results.Accepted(
                $"/api/data-extraction/email/messages/{message.Id}",
                new
                {
                    jobId = job.Id,
                    emailMessageId = message.Id,
                    pricingRequestId = requestId,
                    status = EmailExtractionJobStatus.AwaitingPricing.ToString(),
                }
            );
        });

        group.MapGet("/extraction-jobs", async (
            int? pageNumber,
            int? pageSize,
            EmailExtractionJobStatus? status,
            Guid? emailMessageId,
            ServiceDbContext dbContext,
            CancellationToken cancellationToken
        ) =>
        {
            var page = Math.Max(pageNumber ?? 1, 1);
            var size = Math.Clamp(pageSize ?? 20, 1, 100);
            var query = dbContext.EmailExtractionJobs.AsNoTracking().Where(x => !x.IsDeleted);

            if (status.HasValue)
            {
                query = query.Where(x => x.Status == status.Value);
            }

            if (emailMessageId.HasValue)
            {
                query = query.Where(x => x.EmailMessageId == emailMessageId.Value);
            }

            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .OrderByDescending(x => x.CreatedAtUtc)
                .Skip((page - 1) * size)
                .Take(size)
                .Select(x => new EmailExtractionJobResponse(
                    x.Id,
                    x.EmailMessageId,
                    x.EmailAttachmentId,
                    x.SourceType.ToString(),
                    x.ProvisionalPricingImportId,
                    x.ExtractionExecutionId,
                    x.PricingImportBatchId,
                    x.AiRequestId,
                    x.AiExecutionId,
                    x.PricingRequestId,
                    x.Status.ToString(),
                    x.ConfidenceScore,
                    x.AttemptCount,
                    x.NextAttemptAtUtc,
                    x.LeaseExpiresAtUtc,
                    x.LastHeartbeatAtUtc,
                    x.LastErrorCode,
                    x.ErrorMessage,
                    x.StartedAt,
                    x.FinishedAt
                ))
                .ToListAsync(cancellationToken);

            return Results.Ok(new { pageNumber = page, pageSize = size, total, items });
        });

        return app;
    }


    private static ExtractedPricingRowDto ToExtractedPricingRowDto(
        Dhole.DataExtraction.Domain.Extraction.Entities.PricingExtractionRecord record
    )
    {
        return new ExtractedPricingRowDto(
            record.Id,
            record.ExtractionExecutionId,
            record.SourceDocumentId,
            record.SourceSheetName,
            record.SourceRowNumber,
            record.OriginPort,
            record.PortOfExit,
            record.DestinationPort,
            record.ContainerType,
            record.Carrier,
            record.Agent,
            record.Commodity,
            record.Currency,
            record.FreeDays,
            record.TransitDays,
            record.ValidFrom,
            record.ValidTo,
            record.OceanFreight,
            record.OriginCharges,
            record.DestinationCharges,
            record.Surcharges,
            record.TotalCost,
            record.TotalSale,
            record.Profit,
            record.Margin,
            record.SpaceComment,
            record.Remarks,
            record.Status.ToString(),
            record.RawJson,
            ToCatalogReference(record.OriginPortReference),
            ToCatalogReference(record.PortOfExitReference),
            ToCatalogReference(record.DestinationPortReference),
            ToCatalogReference(record.ContainerTypeReference),
            ToCatalogReference(record.CarrierReference),
            ToCatalogReference(record.AgentReference),
            ToCatalogReference(record.CurrencyReference)
        );
    }

    private static CatalogReferenceDto? ToCatalogReference(
        Dhole.DataExtraction.Domain.Extraction.ValueObjects.CatalogItemReference? reference
    )
    {
        return reference is null
            ? null
            : new CatalogReferenceDto(
                reference.CatalogItemId,
                reference.CatalogGroupSlug,
                reference.Code,
                reference.Slug,
                reference.Name,
                reference.RawValue
            );
    }


    public sealed record UpsertEmailAccountRequest(
        string Name,
        string EmailAddress,
        EmailProviderType ProviderType,
        string? Host,
        int Port,
        bool UseSsl,
        string Username,
        string SecretReference,
        string FolderName,
        int PollingIntervalMinutes,
        bool AutoProcess,
        bool AutoSendToPricing,
        decimal AutoSendMinConfidence,
        bool ProcessBodyWhenNoSupportedAttachments,
        bool ProcessBodyEvenWithAttachments,
        string? AllowedSenders
    );

    public sealed record SetEmailAccountActiveRequest(bool IsActive);
    public sealed record IgnoreEmailMessageRequest(string? Reason);

    public sealed record EmailAccountResponse(
        Guid Id,
        string Name,
        string EmailAddress,
        string ProviderType,
        string Host,
        int Port,
        bool UseSsl,
        string Username,
        string SecretReference,
        string FolderName,
        int PollingIntervalMinutes,
        bool AutoProcess,
        bool AutoSendToPricing,
        decimal AutoSendMinConfidence,
        bool ProcessBodyWhenNoSupportedAttachments,
        bool ProcessBodyEvenWithAttachments,
        string? AllowedSenders,
        bool IsActive,
        long? LastProcessedUid,
        DateTime? LastSyncAt,
        string? LastSyncError
    );

    public sealed record EmailMessageListResponse(
        Guid Id,
        Guid EmailIngestionAccountId,
        string? FromName,
        string FromAddress,
        string Subject,
        DateTime ReceivedAt,
        bool HasAttachments,
        string Status,
        decimal? ClassificationConfidence,
        string? ClassificationReason,
        string? ErrorMessage
    );

    public sealed record EmailMessageDetailResponse(
        Guid Id,
        Guid EmailIngestionAccountId,
        string ExternalMessageId,
        long? Uid,
        string? MessageIdHeader,
        string? FromName,
        string FromAddress,
        string? ToAddresses,
        string? CcAddresses,
        string Subject,
        string? BodyText,
        string? BodyHtml,
        DateTime ReceivedAt,
        bool HasAttachments,
        string? RawEmailStoragePath,
        string Status,
        decimal? ClassificationConfidence,
        string? ClassificationReason,
        string? ErrorMessage,
        IReadOnlyCollection<EmailAttachmentResponse> Attachments,
        IReadOnlyCollection<EmailExtractionJobResponse> Jobs
    );

    public sealed record EmailAttachmentResponse(
        Guid Id,
        string FileName,
        string? ContentType,
        string? FileExtension,
        long SizeBytes,
        string FileHash,
        string SourceFileType,
        string Status,
        string? ErrorMessage,
        string StoragePath
    );

    public sealed record EmailExtractionJobResponse(
        Guid Id,
        Guid EmailMessageId,
        Guid? EmailAttachmentId,
        string SourceType,
        Guid ProvisionalPricingImportId,
        Guid? ExtractionExecutionId,
        Guid? PricingImportBatchId,
        Guid? AiRequestId,
        Guid? AiExecutionId,
        Guid? PricingRequestId,
        string Status,
        decimal? ConfidenceScore,
        int AttemptCount,
        DateTime? NextAttemptAtUtc,
        DateTime? LeaseExpiresAtUtc,
        DateTime? LastHeartbeatAtUtc,
        string? LastErrorCode,
        string? ErrorMessage,
        DateTime? StartedAt,
        DateTime? FinishedAt
    );
}
