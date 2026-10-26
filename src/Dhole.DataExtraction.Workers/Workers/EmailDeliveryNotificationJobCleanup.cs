using Dhole.DataExtraction.Domain.Emails;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Workers.Workers;

/// <summary>
/// Clears delivery notifications imported before the ignore policy was enabled,
/// so they cannot keep occupying the extraction queue or be retried.
/// </summary>
internal static class EmailDeliveryNotificationJobCleanup
{
    public static async Task<int> IgnoreAsync(
        ServiceDbContext dbContext,
        CancellationToken cancellationToken
    )
    {
        // Restrict the database scan, then apply the exact shared policy in memory.
        // Process in bounded batches so older notifications are cleaned up progressively.
        var candidates = await dbContext.EmailMessages
            .Where(message =>
                !message.IsDeleted
                && message.Status != EmailMessageStatus.Ignored
                && message.Status != EmailMessageStatus.Extracted
                && (
                    EF.Functions.ILike(message.Subject, EmailSubjectIgnorePolicy.DeliveryDelaySubject)
                    || EF.Functions.ILike(message.Subject, EmailSubjectIgnorePolicy.DeliveryFailureSubject)
                    || EF.Functions.ILike(message.FromAddress, "mailer-daemon@%")
                    || EF.Functions.ILike(message.FromAddress, "postmaster@%")
                    || (message.FromName != null && EF.Functions.ILike(message.FromName, "Dhole%"))
                    || EF.Functions.ILike(message.Subject, "%QUO-%")
                    || (message.BodyText != null && EF.Functions.ILike(message.BodyText, "%Dhole%"))
                    || (message.BodyHtml != null
                        && (EF.Functions.ILike(message.BodyHtml, "%Dhole%")
                            || EF.Functions.ILike(message.BodyHtml, "%sistema.logisticacastrofallas.com/pricing/%")))
                )
            )
            .OrderBy(message => message.ReceivedAt)
            .Take(200)
            .ToListAsync(cancellationToken);

        var ignored = 0;
        foreach (var message in candidates)
        {
            var ignoreReason = EmailSubjectIgnorePolicy.GetIgnoreReason(
                message.Subject,
                message.FromAddress,
                message.FromName,
                message.BodyText,
                message.BodyHtml
            );
            if (ignoreReason is null)
            {
                continue;
            }

            var jobs = await dbContext.EmailExtractionJobs
                .Where(job => job.EmailMessageId == message.Id && !job.IsDeleted)
                .ToListAsync(cancellationToken);

            // Do not modify records that have already been handed off to Pricing.
            if (jobs.Any(job => job.Status is
                EmailExtractionJobStatus.Extracting or
                EmailExtractionJobStatus.AwaitingPricing or EmailExtractionJobStatus.SentToPricing))
            {
                continue;
            }

            foreach (var job in jobs)
            {
                if (job.Status != EmailExtractionJobStatus.Ignored)
                {
                    job.MarkIgnored(ignoreReason);
                }
            }

            message.MarkIgnored(ignoreReason);
            ignored++;
        }

        if (ignored > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return ignored;
    }
}
