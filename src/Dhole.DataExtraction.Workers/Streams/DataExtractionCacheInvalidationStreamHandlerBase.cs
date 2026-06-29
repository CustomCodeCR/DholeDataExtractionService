using System.Text.Json;
using CustomCodeFramework.Redis.Streams.Abstractions;
using CustomCodeFramework.Redis.Streams.Messages;
using Dhole.DataExtraction.Application.Abstractions.Cache;

namespace Dhole.DataExtraction.Workers.Streams;

internal abstract class DataExtractionCacheInvalidationStreamHandlerBase(
    IDataExtractionCacheService cache,
    ILogger logger
) : IRedisStreamMessageHandler
{
    public abstract string MessageType { get; }

    public async Task HandleAsync(
        RedisStreamEnvelope envelope,
        CancellationToken cancellationToken = default
    )
    {
        var extractionExecutionId = TryGetGuid(envelope, "extractionExecutionId")
            ?? TryGetGuid(envelope, "ExtractionExecutionId");
        var fileHash = TryGetString(envelope, "fileHash") ?? TryGetString(envelope, "FileHash");

        if (extractionExecutionId.HasValue)
        {
            await cache.RemoveExtractionCacheAsync(
                extractionExecutionId.Value,
                fileHash,
                cancellationToken
            );
        }
        else if (!string.IsNullOrWhiteSpace(fileHash))
        {
            await cache.RemoveSourceDocumentByHashAsync(fileHash, cancellationToken);
        }

        logger.LogInformation(
            "DataExtraction cache invalidated for event {MessageType}, message {MessageId}.",
            envelope.MessageType,
            envelope.MessageId
        );
    }

    private static Guid? TryGetGuid(RedisStreamEnvelope envelope, string propertyName)
    {
        var value = TryGetString(envelope, propertyName);
        return Guid.TryParse(value, out var guid) ? guid : null;
    }

    private static string? TryGetString(RedisStreamEnvelope envelope, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(envelope.PayloadJson))
        {
            return null;
        }

        using var document = JsonDocument.Parse(envelope.PayloadJson);
        return document.RootElement.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}
