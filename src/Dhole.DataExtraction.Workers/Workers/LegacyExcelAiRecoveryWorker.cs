using CustomCodeFramework.Workers.Abstractions;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Workers.Workers;

/// <summary>
/// Requeues legacy Excel attachments that were incorrectly rejected by older
/// deployments whose AI allow-list only contained XLSX. This worker is intentionally
/// narrow so unrelated failed jobs are never retried automatically.
/// </summary>
internal sealed class LegacyExcelAiRecoveryWorker(
    ServiceDbContext dbContext,
    IConfiguration configuration,
    ILogger<LegacyExcelAiRecoveryWorker> logger
) : IBackgroundWorker
{
    private const string OldAiMessage =
        "AI solo puede normalizar cuerpo de correo, PDF, CSV o XLSX";
    private const string OldDataExtractionMessage =
        "DataExtraction solo admite cuerpo de correo, PDF, CSV o XLSX";

    public string Name => "data-extraction.legacy-excel-ai-recovery";

    public async Task ExecuteAsync(
        IWorkerExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        if (!ReadBoolean(configuration["EmailIngestion:Enabled"], false))
        {
            return;
        }

        var candidates = await (
            from job in dbContext.EmailExtractionJobs
            join attachment in dbContext.EmailAttachments
                on job.EmailAttachmentId equals (Guid?)attachment.Id
            where !job.IsDeleted
                && !attachment.IsDeleted
                && job.SourceType == EmailContentSourceType.Attachment
                && (job.Status == EmailExtractionJobStatus.Failed
                    || job.Status == EmailExtractionJobStatus.NeedsReview)
                && attachment.SourceFileType == SourceFileType.Excel
                && attachment.FileExtension != null
                && (attachment.FileExtension.ToLower() == ".xls"
                    || attachment.FileExtension.ToLower() == ".xlsm")
                && job.ErrorMessage != null
                && (job.ErrorMessage.Contains(OldAiMessage)
                    || job.ErrorMessage.Contains(OldDataExtractionMessage))
            orderby job.CreatedAtUtc
            select job
        )
            .Take(250)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return;
        }

        var messageIds = new HashSet<Guid>();
        foreach (var job in candidates)
        {
            job.Retry();
            messageIds.Add(job.EmailMessageId);
        }

        foreach (var messageId in messageIds)
        {
            await EmailJobStateCoordinator.RecalculateAsync(
                dbContext,
                messageId,
                cancellationToken
            );
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogWarning(
            "Se reencolaron {JobCount} adjuntos XLS/XLSM rechazados por la allow-list antigua de AI.",
            candidates.Count
        );
    }

    private static bool ReadBoolean(string? value, bool fallback)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
