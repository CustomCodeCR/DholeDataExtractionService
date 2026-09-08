using CustomCodeFramework.Workers.Abstractions;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Workers.Workers;

/// <summary>
/// Re-runs the deterministic extraction when an email has remained in the AI hand-off
/// for too long. This is intentionally a DataExtraction-side safety net: a provider,
/// worker restart, stale Redis event or orphaned AI lease must never leave the mailbox
/// permanently in "Procesando con AI".
/// </summary>
internal sealed class StaleAiEmailRecoveryWorker(
    ServiceDbContext dbContext,
    IConfiguration configuration,
    ILogger<StaleAiEmailRecoveryWorker> logger
) : IBackgroundWorker
{
    public string Name => "data-extraction.stale-ai-email-recovery";

    public async Task ExecuteAsync(
        IWorkerExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        if (!ReadBoolean(configuration["EmailIngestion:Enabled"], false)
            || !ReadBoolean(configuration["AI:AsyncEmail:Enabled"], true))
        {
            return;
        }

        // Never allow the UI to sit indefinitely in AwaitingAi/AiProcessing. Fifteen
        // minutes is deliberately longer than a normal deterministic extraction but
        // short enough to recover a broken AI hand-off without operator intervention.
        var configuredMinutes = ReadPositiveInt(
            configuration["AI:AsyncEmail:StaleAiReprocessMinutes"],
            15
        );
        var staleAfterMinutes = Math.Clamp(configuredMinutes, 5, 120);
        var staleBefore = DateTime.UtcNow.AddMinutes(-staleAfterMinutes);

        var candidates = await dbContext.EmailExtractionJobs
            .Where(job =>
                !job.IsDeleted
                && (job.Status == EmailExtractionJobStatus.AwaitingAi
                    || job.Status == EmailExtractionJobStatus.AiProcessing)
                && ((job.StartedAt.HasValue && job.StartedAt.Value < staleBefore)
                    || (!job.StartedAt.HasValue && job.CreatedAtUtc < staleBefore))
            )
            .OrderBy(job => job.StartedAt ?? job.CreatedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return;
        }

        var messageIds = new HashSet<Guid>();
        foreach (var job in candidates)
        {
            var previousAiRequestId = job.AiRequestId;
            var previousStatus = job.Status;

            // Retry clears the stale AI request/execution ids and returns the job to
            // Pending. The current deterministic parser then gets the first chance to
            // finish it without AI; if AI is still genuinely required, a fresh
            // idempotent request is created.
            job.Retry();
            messageIds.Add(job.EmailMessageId);

            logger.LogWarning(
                "Se reencoló trabajo {EmailExtractionJobId} atascado en {PreviousStatus}. "
                    + "Solicitud AI anterior {AiRequestId}; límite {StaleAfterMinutes} min.",
                job.Id,
                previousStatus,
                previousAiRequestId,
                staleAfterMinutes
            );
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
            "Se recuperaron {JobCount} trabajos de correo que permanecían demasiado tiempo esperando AI.",
            candidates.Count
        );
    }

    private static bool ReadBoolean(string? value, bool fallback)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }
}
