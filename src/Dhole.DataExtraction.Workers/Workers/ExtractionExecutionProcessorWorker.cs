using CustomCodeFramework.Workers.Abstractions;

namespace Dhole.DataExtraction.Workers.Workers;

internal sealed class ExtractionExecutionProcessorWorker(
    ILogger<ExtractionExecutionProcessorWorker> logger
) : IBackgroundWorker
{
    public string Name => "data-extraction.execution-processor";

    public Task ExecuteAsync(
        IWorkerExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        logger.LogDebug("ExtractionExecutionProcessorWorker is idle. Current extraction flow is request/response by gRPC.");
        return Task.CompletedTask;
    }
}
