using Dhole.DataExtraction.Application.Abstractions.Cache;

namespace Dhole.DataExtraction.Workers.Streams;

internal sealed class ExtractionExecutionCompletedStreamHandler(
    IDataExtractionCacheService cache,
    ILogger<ExtractionExecutionCompletedStreamHandler> logger
) : DataExtractionCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "data-extraction.execution.completed";
}
