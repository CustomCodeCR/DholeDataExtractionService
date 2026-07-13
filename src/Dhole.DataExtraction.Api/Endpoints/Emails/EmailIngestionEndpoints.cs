using Dhole.DataExtraction.Api.Extensions;
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
                    x.Status.ToString(),
                    x.ConfidenceScore,
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

            var hasSupported = false;
            foreach (var attachment in attachments.Where(x => x.SourceFileType.ToString() != "Unknown"))
            {
                dbContext.EmailExtractionJobs.Add(EmailExtractionJob.CreateAttachmentJob(message.Id, attachment.Id, httpContext.GetCurrentUserId()));
                hasSupported = true;
            }

            if (!hasSupported || !string.IsNullOrWhiteSpace(message.BodyHtml) || !string.IsNullOrWhiteSpace(message.BodyText))
            {
                dbContext.EmailExtractionJobs.Add(EmailExtractionJob.CreateBodyJob(message.Id, httpContext.GetCurrentUserId()));
            }

            message.MarkQueued(message.ClassificationConfidence ?? 50m, "Reprocesamiento solicitado manualmente.", httpContext.GetCurrentUserId());
            await dbContext.SaveChangesAsync(cancellationToken);
            return Results.Accepted($"/api/data-extraction/email/messages/{message.Id}", new { message.Id });
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
                    x.Status.ToString(),
                    x.ConfidenceScore,
                    x.ErrorMessage,
                    x.StartedAt,
                    x.FinishedAt
                ))
                .ToListAsync(cancellationToken);

            return Results.Ok(new { pageNumber = page, pageSize = size, total, items });
        });

        return app;
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
        string Status,
        decimal? ConfidenceScore,
        string? ErrorMessage,
        DateTime? StartedAt,
        DateTime? FinishedAt
    );
}
