using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Infrastructure.Normalization;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class PricingRecordNormalizerTests
{
    [TestMethod]
    public async Task NormalizeAsync_WhenCurrencyIsMissing_DefaultsToUsd()
    {
        var row = new MappedPricingRow(
            "Rates",
            2,
            new Dictionary<string, string?>
            {
                ["OriginPort"] = "Shanghai",
                ["PortOfExit"] = "Moin",
                ["ContainerType"] = "40HC",
                ["Carrier"] = "MSC",
                ["ValidFrom"] = "01-Aug-2026",
                ["ValidTo"] = "31-Aug-2026",
                ["OceanFreight"] = "6210",
            }
        );

        var records = await new PricingRecordNormalizer().NormalizeAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            [row]
        );

        Assert.AreEqual("USD", records.Single().Currency);
    }

    [TestMethod]
    public async Task NormalizeAsync_WhenPdfColumnsAreShifted_RecoversValidityAndFreeDays()
    {
        var row = new MappedPricingRow(
            "PDF Visual Table 1",
            7,
            new Dictionary<string, string?>
            {
                ["OriginPort"] = "Lianyungang",
                ["PortOfExit"] = "Moin",
                ["ContainerType"] = "40HC",
                ["Carrier"] = "Maersk",
                ["ValidFrom"] = "14",
                ["ValidTo"] = "01-Aug-2026 31-Aug-2026",
                ["OceanFreight"] = "$6,430",
            }
        );

        var records = await new PricingRecordNormalizer().NormalizeAsync(
            Guid.NewGuid(),
            Guid.NewGuid(),
            [row]
        );

        var record = records.Single();
        Assert.AreEqual("USD", record.Currency);
        Assert.AreEqual(14, record.FreeDays);
        Assert.AreEqual(new DateTime(2026, 8, 1), record.ValidFrom);
        Assert.AreEqual(new DateTime(2026, 8, 31), record.ValidTo);
    }

    [TestMethod]
    public void DateNormalizer_WhenGivenSmallDayCount_DoesNotCreate1900Date()
    {
        Assert.IsNull(DateNormalizer.Normalize("14"));
    }
}
