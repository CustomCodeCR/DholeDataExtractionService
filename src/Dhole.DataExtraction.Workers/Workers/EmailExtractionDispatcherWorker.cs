using CustomCodeFramework.Workers.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Dhole.DataExtraction.Workers.Workers;

/// <summary>
/// Runs independent email extraction jobs in parallel. Each slot has its own
/// DI scope/DbContext, and the database claim uses FOR UPDATE SKIP LOCKED.
/// </summary>
internal sealed class EmailExtractionDispatcherWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<EmailExtractionDispatcherWorker> logger
) : IBackgroundWorker
{
    private const int MaximumConfiguredParallelism = 8;
    private static readonly SemaphoreSlim DispatchGate = new(1, 1);

    public string Name => "data-extraction.email-extraction-dispatcher";

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

        if (!await DispatchGate.WaitAsync(0, cancellationToken))
        {
            logger.LogDebug("Email extraction dispatcher already running; skipping overlapping cycle.");
            return;
        }

        try
        {
            var parallelism = Math.Clamp(
                ReadPositiveInt(configuration["EmailIngestion:MaxConcurrentExtractionJobs"], 1),
                1,
                MaximumConfiguredParallelism
            );
            var jobBudget = ReadPositiveInt(
                configuration["EmailIngestion:MaxExtractionJobsPerRun"],
                50
            );
            var jobsPerSlot = Math.Max(1, (int)Math.Ceiling((double)jobBudget / parallelism));

            // Recovery/reclassification mutates shared jobs and must run once per cycle.
            await using (var maintenanceScope = scopeFactory.CreateAsyncScope())
            {
                var maintenanceWorker =
                    maintenanceScope.ServiceProvider.GetRequiredService<EmailExtractionWorker>();
                await maintenanceWorker.PrepareAsync(cancellationToken);
            }

            logger.LogInformation(
                "Processing email extractions with {Parallelism} parallel slots and {JobBudget} jobs per cycle.",
                parallelism,
                jobBudget
            );

            var tasks = Enumerable.Range(0, parallelism)
                .Select(slot => RunSlotAsync(slot, jobsPerSlot, cancellationToken))
                .ToArray();
            await Task.WhenAll(tasks);
        }
        finally
        {
            DispatchGate.Release();
        }
    }

    private async Task RunSlotAsync(
        int slot,
        int jobsPerSlot,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var worker = scope.ServiceProvider.GetRequiredService<EmailExtractionWorker>();
            await worker.ProcessAvailableJobsAsync(jobsPerSlot, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Email extraction slot {Slot} failed; other slots will continue.",
                slot + 1
            );
        }
    }

    private static bool ReadBoolean(string? value, bool fallback) =>
        bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static int ReadPositiveInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
}
