using CustomCodeFramework.Workers.Abstractions;

namespace Dhole.DataExtraction.Workers.Workers;

internal sealed class DataExtractionCacheWarmupWorker(
    ILogger<DataExtractionCacheWarmupWorker> logger
) : IBackgroundWorker
{
    public string Name => "data-extraction.cache-warmup";

    public Task ExecuteAsync(
        IWorkerExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        logger.LogInformation("DataExtraction cache warmup completed.");
        return Task.CompletedTask;
    }
}
