using System.Data;
using Dhole.DataExtraction.Domain.Emails;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Workers.Workers;

/// <summary>
/// Archives the original email and unsubmitted extraction jobs from previous
/// calendar months. Never deletes source evidence or modifies a Pricing import.
/// Bounded maintenance batches use row locks to avoid duplicate claims.
/// </summary>
internal static class EmailHistoricalMonthCleanup
{
    public const string ArchiveReason =
        "Extracción archivada: el correo pertenece a un mes anterior. " +
        "Se conserva el documento original y el historial de procesamiento.";

    public static async Task<int> ArchiveAsync(
        ServiceDbContext dbContext,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var cutoff = EmailHistoricalMonthPolicy.CurrentMonthStartUtc(DateTime.UtcNow);
        var limit = Math.Clamp(batchSize, 1, 1000);
        var archived = 0;

        await dbContext.ExecuteInRetryableTransactionAsync(
            async () =>
            {
                var messages = await dbContext.EmailMessages
                    .FromSqlInterpolated($"""
                        SELECT *
                        FROM data_extraction."EmailMessages"
                        WHERE received_at < {cutoff}
                          AND is_deleted = FALSE
                          AND status IN ('Received', 'Queued', 'Processing', 'NeedsReview', 'Failed')
                        ORDER BY received_at
                        FOR UPDATE SKIP LOCKED
                        LIMIT {limit}
                        """)
                    .ToListAsync(cancellationToken);

                foreach (var message in messages)
                {
                    var jobs = await dbContext.EmailExtractionJobs
                        .Where(job => !job.IsDeleted && job.EmailMessageId == message.Id)
                        .ToListAsync(cancellationToken);

                    // Do not cancel a Pricing import already accepted for delivery.
                    if (jobs.Any(job => job.Status == EmailExtractionJobStatus.AwaitingPricing
                        || job.PricingRequestId.HasValue
                            && job.Status != EmailExtractionJobStatus.SentToPricing))
                        continue;

                    var hasPricingSuccess = jobs.Any(job =>
                        job.Status == EmailExtractionJobStatus.SentToPricing
                        && job.PricingImportBatchId.HasValue);

                    foreach (var job in jobs)
                    {
                        if (job.Status is EmailExtractionJobStatus.SentToPricing
                            or EmailExtractionJobStatus.Ignored
                            or EmailExtractionJobStatus.AwaitingPricing
                            || job.PricingImportBatchId.HasValue)
                            continue;

                        job.MarkIgnored(ArchiveReason);
                    }

                    if (hasPricingSuccess)
                        message.MarkExtracted();
                    else
                        message.MarkIgnored(ArchiveReason);
                    archived++;
                }

                if (archived > 0)
                    await dbContext.SaveChangesAsync(cancellationToken);
            },
            IsolationLevel.ReadCommitted,
            cancellationToken);

        dbContext.ChangeTracker.Clear();
        return archived;
    }
}
