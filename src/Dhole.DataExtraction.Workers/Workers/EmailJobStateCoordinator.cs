using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Workers.Workers;

internal static class EmailJobStateCoordinator
{
    private static readonly EmailExtractionJobStatus[] ActiveStatuses =
    [
        EmailExtractionJobStatus.Pending,
        EmailExtractionJobStatus.Extracting,
        EmailExtractionJobStatus.AwaitingAi,
        EmailExtractionJobStatus.AiProcessing,
        EmailExtractionJobStatus.ValidatingAiResult,
        EmailExtractionJobStatus.AwaitingPricing,
    ];

    public static async Task RecalculateAsync(
        ServiceDbContext dbContext,
        Guid emailMessageId,
        CancellationToken cancellationToken
    )
    {
        var message = await dbContext.EmailMessages.FirstOrDefaultAsync(
            item => item.Id == emailMessageId && !item.IsDeleted,
            cancellationToken
        );
        if (message is null)
        {
            return;
        }

        var jobs = await dbContext.EmailExtractionJobs
            .Where(item => item.EmailMessageId == emailMessageId && !item.IsDeleted)
            .ToListAsync(cancellationToken);
        if (jobs.Count == 0)
        {
            return;
        }

        // A successful Pricing import means this email has already produced
        // usable rates. Other attachments may still be analysed independently,
        // but they must not keep the parent email labelled "Procesando".
        // Success must win over Pending/AwaitingAi/NeedsReview siblings.
        if (jobs.Any(item => item.Status == EmailExtractionJobStatus.SentToPricing
            && item.PricingImportBatchId.HasValue))
        {
            if (message.Status != EmailMessageStatus.Extracted)
                message.MarkExtracted();
            return;
        }

        // A deferred Pending job is queued, not actively executing AI.
        // Its parent should not be shown as "Procesando" when there are no
        // Extracting/AI/Pricing children. Preserve previously extracted mail.
        if (jobs.Any(item => item.Status == EmailExtractionJobStatus.Pending)
            && jobs.All(item => item.Status is
                EmailExtractionJobStatus.Pending
                or EmailExtractionJobStatus.NeedsReview
                or EmailExtractionJobStatus.Failed
                or EmailExtractionJobStatus.Ignored))
        {
            if (message.Status is not EmailMessageStatus.Queued
                and not EmailMessageStatus.Extracted)
                message.MarkQueued(
                    message.ClassificationConfidence ?? 0m,
                    message.ClassificationReason);
            return;
        }

        if (jobs.Any(item => ActiveStatuses.Contains(item.Status)))
        {
            message.MarkProcessing();
            return;
        }

        if (jobs.All(item => item.Status is EmailExtractionJobStatus.SentToPricing
                or EmailExtractionJobStatus.Ignored))
        {
            if (jobs.Any(item => item.Status == EmailExtractionJobStatus.SentToPricing))
                message.MarkExtracted();
            else
            {
                var invalid = jobs.FirstOrDefault(item =>
                    item.ErrorMessage?.StartsWith(
                        "Tarifa no válida:", StringComparison.OrdinalIgnoreCase) == true);
                if (invalid is not null)
                    message.MarkInvalid(invalid.ErrorMessage);
                else
                    message.MarkIgnored("No se encontraron contenidos tarifarios procesables.");
            }
            return;
        }

        var reviewJob = jobs.FirstOrDefault(item =>
            item.Status == EmailExtractionJobStatus.NeedsReview
        );
        if (reviewJob is not null)
        {
            message.MarkNeedsReview(
                reviewJob.ErrorMessage
                    ?? "Uno o más contenidos del correo requieren revisión."
            );
            return;
        }

        var failedJob = jobs.FirstOrDefault(item =>
            item.Status == EmailExtractionJobStatus.Failed
        );
        if (failedJob is not null)
        {
            message.MarkFailed(
                failedJob.ErrorMessage
                    ?? "Uno o más contenidos del correo no pudieron procesarse."
            );
        }
    }
}
