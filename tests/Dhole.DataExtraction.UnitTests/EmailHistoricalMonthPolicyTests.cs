using Dhole.DataExtraction.Domain.Emails;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class EmailHistoricalMonthPolicyTests
{
    [TestMethod]
    public void NovemberStartsAtSixAmUtc_InCostaRica()
    {
        var now = new DateTime(2026, 11, 6, 17, 0, 0, DateTimeKind.Utc);
        var cutoff = EmailHistoricalMonthPolicy.CurrentMonthStartUtc(now);
        Assert.AreEqual(new DateTime(2026, 11, 1, 6, 0, 0, DateTimeKind.Utc), cutoff);
        Assert.IsTrue(EmailHistoricalMonthPolicy.ShouldArchive(
            new DateTime(2026, 10, 31, 23, 59, 0, DateTimeKind.Utc), now));
        Assert.IsFalse(EmailHistoricalMonthPolicy.ShouldArchive(
            new DateTime(2026, 11, 1, 6, 0, 0, DateTimeKind.Utc), now));
    }

    [TestMethod]
    public void ArchiveUsesOriginalReceipt_NotJobCreationDate()
    {
        var now = new DateTime(2026, 11, 6, 17, 0, 0, DateTimeKind.Utc);
        Assert.IsTrue(EmailHistoricalMonthPolicy.ShouldArchive(
            new DateTime(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc), now));
        Assert.IsFalse(EmailHistoricalMonthPolicy.ShouldArchive(
            new DateTime(2026, 11, 5, 8, 0, 0, DateTimeKind.Utc), now));
    }
}
