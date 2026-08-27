using CustomCodeFramework.Workers.Abstractions;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Workers.Workers;

/// <summary>
/// Cleans historical extraction failures/reviews when another job from the same
/// email was already sent to Pricing and the closed job is demonstrably redundant.
/// This includes AI.NoPricingRows and deterministic fallback results that contain
/// no rate amount plus several missing tariff identity fields.
/// </summary>
internal sealed class RedundantAiNoPricingRowsRecoveryWorker(
    ServiceDbContext dbContext,
    IConfiguration configuration,
    ILogger<RedundantAiNoPricingRowsRecoveryWorker> logger
) : IBackgroundWorker
{
    public string Name => "data-extraction.redundant-ai-no-pricing-rows-recovery";

    public async Task ExecuteAsync(
        IWorkerExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        if (!ReadBoolean(configuration["EmailIngestion:Enabled"], false))
        {
            return;
        }

        var closedJobsWithSuccessfulSibling = await dbContext.EmailExtractionJobs
            .Where(job =>
                !job.IsDeleted
                && (job.Status == EmailExtractionJobStatus.Failed
                    || job.Status == EmailExtractionJobStatus.NeedsReview)
                && dbContext.EmailExtractionJobs.Any(sibling =>
                    !sibling.IsDeleted
                    && sibling.EmailMessageId == job.EmailMessageId
                    && sibling.Id != job.Id
                    && sibling.Status == EmailExtractionJobStatus.SentToPricing
                )
            )
            .OrderBy(job => job.CreatedAtUtc)
            .Take(500)
            .ToListAsync(cancellationToken);

        var candidates = closedJobsWithSuccessfulSibling
            .Where(RedundantEmailJobReviewPolicy.IsRedundantAfterPricingSuccess)
            .Take(250)
            .ToList();

        if (candidates.Count == 0)
        {
            return;
        }

        var messageIds = new HashSet<Guid>();
        foreach (var job in candidates)
        {
            job.MarkIgnored(
                "El contenido no produjo una tarifa adicional utilizable y se archivó porque otro contenido del mismo correo ya fue enviado correctamente a Pricing."
            );
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
        logger.LogInformation(
            "Se archivaron {JobCount} revisiones/fallos redundantes de correos que ya tenían contenido enviado a Pricing.",
            candidates.Count
        );
    }

    private static bool ReadBoolean(string? value, bool fallback)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
