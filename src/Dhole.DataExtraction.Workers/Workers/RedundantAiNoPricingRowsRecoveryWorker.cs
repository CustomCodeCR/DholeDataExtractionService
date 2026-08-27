using CustomCodeFramework.Workers.Abstractions;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Workers.Workers;

/// <summary>
/// Cleans historical AI.NoPricingRows failures when another job from the same
/// email was already sent to Pricing. These jobs are redundant results, not
/// failures of the email as a whole.
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

        var candidates = await dbContext.EmailExtractionJobs
            .Where(job =>
                !job.IsDeleted
                && (job.Status == EmailExtractionJobStatus.Failed
                    || job.Status == EmailExtractionJobStatus.NeedsReview)
                && (
                    job.LastErrorCode == "AI.NoPricingRows"
                    || (job.ErrorMessage != null
                        && job.ErrorMessage.Contains(
                            "AI no encontró filas de tarifas utilizables"
                        ))
                )
                && dbContext.EmailExtractionJobs.Any(sibling =>
                    !sibling.IsDeleted
                    && sibling.EmailMessageId == job.EmailMessageId
                    && sibling.Id != job.Id
                    && sibling.Status == EmailExtractionJobStatus.SentToPricing
                )
            )
            .OrderBy(job => job.CreatedAtUtc)
            .Take(250)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return;
        }

        var messageIds = new HashSet<Guid>();
        foreach (var job in candidates)
        {
            job.MarkIgnored(
                "El contenido no produjo filas tarifarias adicionales y se archivó porque otro contenido del mismo correo ya fue enviado a Pricing."
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
            "Se archivaron {JobCount} fallos AI.NoPricingRows redundantes de correos que ya tenían contenido enviado a Pricing.",
            candidates.Count
        );
    }

    private static bool ReadBoolean(string? value, bool fallback)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
