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

        // An uncompleted request with no completion event cannot protect an email
        // forever. Allow ample time for model execution and retries before closing
        // an orphaned hand-off; do not assume that every AwaitingAi is running.
        var orphanAfterMinutes = Math.Clamp(
            ReadPositiveInt(configuration["AI:AsyncEmail:OrphanRequestMinutes"], 60),
            30,
            720
        );
        var orphanBefore = DateTime.UtcNow.AddMinutes(-orphanAfterMinutes);

        var candidates = await dbContext.EmailExtractionJobs
            .Where(job =>
                !job.IsDeleted
                && (job.Status == EmailExtractionJobStatus.AwaitingAi
                    || job.Status == EmailExtractionJobStatus.AiProcessing)
                && ((job.StartedAt.HasValue && job.StartedAt.Value < staleBefore)
                    || (!job.StartedAt.HasValue && job.CreatedAtUtc < staleBefore))
                // Keep recent active requests intact, even if the job StartedAt is
                // old from a previous extraction attempt. Very old open requests
                // must not block recovery indefinitely after lost Redis events.
                && !dbContext.EmailAiAnalysisRequests.Any(request =>
                    request.EmailExtractionJobId == job.Id
                    && !request.CompletedAtUtc.HasValue
                    && request.CreatedAtUtc >= orphanBefore)
            )
            .OrderBy(job => job.StartedAt ?? job.CreatedAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return;
        }

        // Resolve requests in a single query rather than one lookup per email.
        var candidateJobIds = candidates.Select(job => job.Id).ToArray();
        var openRequestsByJob = (await dbContext.EmailAiAnalysisRequests
            .Where(request => candidateJobIds.Contains(request.EmailExtractionJobId)
                && !request.CompletedAtUtc.HasValue)
            .ToListAsync(cancellationToken))
            .GroupBy(request => request.EmailExtractionJobId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var messageIds = new HashSet<Guid>();
        var orphanedRequests = 0;
        foreach (var job in candidates)
        {
            var previousAiRequestId = job.AiRequestId;
            var previousStatus = job.Status;

            if (openRequestsByJob.TryGetValue(job.Id, out var oldRequests))
            {
                // A truly orphaned request is terminal for this hand-off, NOT an
                // excuse to resubmit the same payload forever. Preserve the job,
                // email and audit records so an operator can retry explicitly.
                foreach (var request in oldRequests)
                {
                    request.MarkCompleted();
                    orphanedRequests++;
                }

                job.MarkNeedsReview(
                    job.ExtractionExecutionId,
                    job.ConfidenceScore ?? 0m,
                    $"La solicitud de IA superó {orphanAfterMinutes} minutos sin respuesta. Revise el contenido o reintente el análisis.",
                    "AI.EmailRequestOrphaned"
                );
            }
            else
            {
                // The request already closed but its result did not advance the job.
                // Retry deterministic extraction using the existing provisional key.
                job.Retry();
            }

            messageIds.Add(job.EmailMessageId);
            logger.LogWarning(
                "Se concilió trabajo {EmailExtractionJobId} atascado en {PreviousStatus}. "
                    + "Solicitud AI anterior {AiRequestId}; solicitudes huérfanas {OrphanCount}.",
                job.Id,
                previousStatus,
                previousAiRequestId,
                oldRequests?.Length ?? 0
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
            "Se conciliaron {JobCount} trabajos atascados esperando IA; {OrphanRequestCount} solicitudes huérfanas cerradas.",
            candidates.Count,
            orphanedRequests
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
