using CustomCodeFramework.Workers.Abstractions;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Workers.Workers;

/// <summary>
/// Reconciles extraction jobs that were returned to Pending while an AI request for the
/// same job is still active. Without this guard, EmailExtractionWorker can create a new
/// RequestId for the same RequestHash; DholeAIService correctly deduplicates that payload,
/// and the eventual completion of the original request is then treated as stale by
/// DataExtraction. The result is an endless "Esperando IA" / redispatch loop.
/// </summary>
internal sealed class ActiveAiRequestRecoveryWorker(
    ServiceDbContext dbContext,
    IConfiguration configuration,
    ILogger<ActiveAiRequestRecoveryWorker> logger
) : IBackgroundWorker
{
    public string Name => "data-extraction.active-ai-request-recovery";

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

        var jobs = await dbContext.EmailExtractionJobs
            .Where(job =>
                !job.IsDeleted
                && job.Status == EmailExtractionJobStatus.Pending)
            .OrderBy(job => job.CreatedAtUtc)
            .Take(250)
            .ToListAsync(cancellationToken);

        if (jobs.Count == 0)
        {
            return;
        }

        // Resolve active requests for the entire pending batch in one query.
        // Previously this caused up to 250 extra database round trips per tick.
        var pendingJobIds = jobs.Select(job => job.Id).ToArray();
        var latestActiveRequests = (await dbContext.EmailAiAnalysisRequests
            .AsNoTracking()
            .Where(request =>
                pendingJobIds.Contains(request.EmailExtractionJobId)
                && !request.CompletedAtUtc.HasValue)
            .OrderByDescending(request => request.CreatedAtUtc)
            .ToListAsync(cancellationToken))
            .GroupBy(request => request.EmailExtractionJobId)
            .ToDictionary(group => group.Key, group => group.First());

        var recovered = 0;
        foreach (var job in jobs)
        {
            if (!latestActiveRequests.TryGetValue(job.Id, out var activeRequest))
            {
                continue;
            }

            job.RestoreAwaitingAi(
                activeRequest.Id,
                activeRequest.ExtractionExecutionId,
                activeRequest.RequestHash
            );
            recovered++;

            logger.LogWarning(
                "Se restauró la solicitud AI activa {AiRequestId} para el trabajo {EmailExtractionJobId}; "
                    + "se evitó crear otro RequestId para el mismo análisis.",
                activeRequest.Id,
                job.Id
            );
        }

        if (recovered > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogWarning(
                "Se reconciliaron {RecoveredCount} trabajos pendientes que ya tenían una solicitud AI activa.",
                recovered
            );
        }
    }

    private static bool ReadBoolean(string? value, bool fallback)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
