using System.Text.Json;
using CustomCodeFramework.Redis.Streams.Messages;

namespace Dhole.DataExtraction.Workers.Streams;

internal static class AsyncEmailStreamPayloadReader
{
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web
    )
    {
        PropertyNameCaseInsensitive = true,
    };

    public static T Read<T>(RedisStreamEnvelope envelope)
    {
        using var document = JsonDocument.Parse(envelope.PayloadJson);
        var root = Unwrap(document.RootElement);
        return root.Deserialize<T>(JsonOptions)
            ?? throw new InvalidOperationException(
                $"El evento '{envelope.MessageType}' no contiene un payload válido."
            );
    }

    public static T ReadJson<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
            ?? throw new InvalidOperationException(
                "No fue posible deserializar el payload persistido."
            );
    }

    private static JsonElement Unwrap(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return root;
        }

        foreach (var name in new[] { "payload", "data", "eventData" })
        {
            foreach (var property in root.EnumerateObject())
            {
                if (
                    string.Equals(
                        property.Name,
                        name,
                        StringComparison.OrdinalIgnoreCase
                    )
                    && property.Value.ValueKind == JsonValueKind.Object
                )
                {
                    return property.Value;
                }
            }
        }

        return root;
    }
}
