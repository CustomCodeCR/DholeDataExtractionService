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

        var recovered = 0;
        foreach (var job in jobs)
        {
            var activeRequest = await dbContext.EmailAiAnalysisRequests
                .Where(request =>
                    request.EmailExtractionJobId == job.Id
                    && !request.CompletedAtUtc.HasValue)
                .OrderByDescending(request => request.CreatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (activeRequest is null)
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
