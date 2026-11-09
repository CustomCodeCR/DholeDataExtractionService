using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Dhole.DataExtraction.Persistence.Messaging;

/// <summary>
/// F6 metadata-only processing invalidations. Never include email bodies,
/// document contents, AI prompts, model output, tokens or private job URLs.
/// Consumers must reconcile with owner-authorized REST, not apply mutations.
/// </summary>
public static class DataExtractionRealtimeInvalidation
{
    public const string ReleaseId = "rt-20261107-01";
    public const string Stream = "dhole.data-extraction.realtime.v1";
    private static long _lastMicrosecond;

    public static bool IsEnabled(IConfiguration? config) =>
        config?.GetValue<bool>("Features:UniversalRealtimeEnabled") == true &&
        config.GetValue<bool>("Realtime:Outbox:Enabled") &&
        config.GetValue<bool>("Realtime:BatchApproved") &&
        string.Equals(config["Realtime:ReleaseId"], ReleaseId, StringComparison.Ordinal);

    public static string EventType(string resource) => resource switch
    {
        "execution" => "extraction.execution.changed.v1",
        "emailjob" => "extraction.emailjob.changed.v1",
        "airequest" => "extraction.airequest.changed.v1",
        _ => throw new ArgumentException("Invalid DataExtraction resource kind.", nameof(resource))
    };

    // A local cursor hint; versions across nodes must be reconciled through REST.
    public static long NextVersionHint()
    {
        while (true)
        {
            var now = (DateTimeOffset.UtcNow.Ticks - DateTimeOffset.UnixEpoch.Ticks) / 10;
            var previous = Volatile.Read(ref _lastMicrosecond);
            var next = Math.Max(now, previous + 1);
            if (Interlocked.CompareExchange(ref _lastMicrosecond, next, previous) == previous)
                return next;
        }
    }

    public static string Serialize(Guid eventId, Guid aggregateId, string resource,
        long version, DateTimeOffset utc, string correlationId)
    {
        if (eventId == Guid.Empty || aggregateId == Guid.Empty ||
            version <= 0 || utc.Offset != TimeSpan.Zero ||
            string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("Invalid DataExtraction realtime event.");
        var eventType = EventType(resource);
        return JsonSerializer.Serialize(new
        {
            eventId,
            eventType,
            schemaVersion = 1,
            aggregateType = resource,
            aggregateId = aggregateId.ToString("D"),
            aggregateVersion = version,
            occurredAtUtc = utc,
            producer = "DholeDataExtractionService",
            correlationId,
            audienceRef = $"extraction:{resource}:{aggregateId:D}",
            payload = new { changedFields = new[] { "resource" } }
        });
    }
}
