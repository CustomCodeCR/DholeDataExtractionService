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

        if (jobs.Any(item => ActiveStatuses.Contains(item.Status)))
        {
            message.MarkProcessing();
            return;
        }

        if (
            jobs.All(item =>
                item.Status
                is EmailExtractionJobStatus.SentToPricing
                    or EmailExtractionJobStatus.Ignored
            )
        )
        {
            message.MarkExtracted();
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
