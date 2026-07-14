using Dhole.DataExtraction.Domain.Extraction.Entities;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class PricingExtractionRecordTests
{
    [TestMethod]
    public void Create_WhenPortOfExitIsMissing_UsesDestinationPortAsPortOfExit()
    {
        var record = PricingExtractionRecord.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Rates",
            2,
            "SHANGHAI",
            null,
            "CALDERA",
            "40HC",
            "MAERSK",
            "WWL",
            "General",
            "USD",
            7,
            22,
            DateTime.UtcNow.Date,
            DateTime.UtcNow.Date.AddDays(30),
            1200m,
            100m,
            75m,
            25m,
            1400m,
            1600m,
            200m,
            12.5m,
            null,
            null,
            "{}",
            null
        );

        Assert.AreEqual("CALDERA", record.PortOfExit);
        Assert.AreEqual(record.DestinationPort, record.PortOfExit);
    }
}
