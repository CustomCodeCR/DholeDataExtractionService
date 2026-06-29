using Dhole.DataExtraction.Application.Abstractions.Cache;

namespace Dhole.DataExtraction.Workers.Streams;

internal sealed class ExtractionExecutionFailedStreamHandler(
    IDataExtractionCacheService cache,
    ILogger<ExtractionExecutionFailedStreamHandler> logger
) : DataExtractionCacheInvalidationStreamHandlerBase(cache, logger)
{
    public override string MessageType => "data-extraction.execution.failed";
}
