using System.Text.Json;
using Dhole.DataExtraction.Persistence.Messaging;
using Microsoft.Extensions.Configuration;

namespace Dhole.DataExtraction.IntegrationTests;

[TestClass]
public sealed class F6DataExtractionRealtimeContractTests
{
    private static IConfiguration Build(bool approved)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Features:UniversalRealtimeEnabled"] = "true",
            ["Realtime:Outbox:Enabled"] = "true",
            ["Realtime:BatchApproved"] = approved.ToString(),
            ["Realtime:ReleaseId"] = DataExtractionRealtimeInvalidation.ReleaseId
        }).Build();

    [TestMethod]
    public void Four_release_gates_disable_unapproved_event_producers()
    {
        Assert.IsFalse(DataExtractionRealtimeInvalidation.IsEnabled(null));
        Assert.IsFalse(DataExtractionRealtimeInvalidation.IsEnabled(Build(false)));
        var config = Build(true);
        Assert.IsTrue(DataExtractionRealtimeInvalidation.IsEnabled(config));
        config["Realtime:ReleaseId"] = "wrong";
        Assert.IsFalse(DataExtractionRealtimeInvalidation.IsEnabled(config));
        config["Realtime:ReleaseId"] = DataExtractionRealtimeInvalidation.ReleaseId;
        config["Realtime:Outbox:Enabled"] = "false";
        Assert.IsFalse(DataExtractionRealtimeInvalidation.IsEnabled(config));
    }

    [TestMethod]
    public void Each_processing_resource_emits_only_non_sensitive_identity_metadata()
    {
        var resourceId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        foreach (var kind in new[] { "execution", "emailjob", "airequest" })
        {
            var json = DataExtractionRealtimeInvalidation.Serialize(eventId, resourceId,
                kind, DataExtractionRealtimeInvalidation.NextVersionHint(),
                DateTimeOffset.UtcNow, "f6-extraction-test");
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.AreEqual("extraction." + kind + ".changed.v1",
                root.GetProperty("eventType").GetString());
            Assert.AreEqual($"extraction:{kind}:{resourceId:D}",
                root.GetProperty("audienceRef").GetString());
            Assert.IsTrue(root.GetProperty("aggregateVersion").GetInt64() > 0);
            CollectionAssert.AreEquivalent(new[] { "changedFields" },
                root.GetProperty("payload").EnumerateObject().Select(p => p.Name).ToArray());
            foreach (var forbidden in new[] {"email", "body", "prompt", "output", "token", "password", "payloadUrl"})
                Assert.IsFalse(json.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
        }
    }

    [TestMethod]
    public void Invalid_identifiers_and_unsupported_kinds_fail_closed()
    {
        var id = Guid.NewGuid();
        var v = DataExtractionRealtimeInvalidation.NextVersionHint();
        Assert.IsTrue(DataExtractionRealtimeInvalidation.NextVersionHint() > v);
        Assert.ThrowsExactly<ArgumentException>(() =>
            DataExtractionRealtimeInvalidation.EventType("private"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            DataExtractionRealtimeInvalidation.Serialize(Guid.Empty, id, "execution", v,
                DateTimeOffset.UtcNow, "x"));
    }
}
