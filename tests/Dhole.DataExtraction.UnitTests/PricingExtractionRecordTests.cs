using Dhole.DataExtraction.Domain.Extraction.Entities;
using Dhole.DataExtraction.Domain.Extraction.ValueObjects;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class PricingExtractionRecordTests
{
    [TestMethod]
    public void Create_WhenPortOfExitIsMissing_DoesNotInferItFromOfficialPod()
    {
        var record = CreateRecord(currency: "USD");

        Assert.IsNull(record.PortOfExit);
        Assert.AreEqual("CALDERA", record.DestinationPort);
    }

    [TestMethod]
    public void ApplyCatalogReferences_WhenCurrencyHasLongDisplayName_PersistsCanonicalCode()
    {
        var record = CreateRecord(currency: "US Dollars");
        var currencyReference = CatalogItemReference.Create(
            Guid.NewGuid(),
            "currencies",
            "USD",
            "usd",
            "Dólar estadounidense",
            "US Dollars"
        );

        record.ApplyCatalogReferences(
            originPortReference: null,
            portOfExitReference: null,
            destinationPortReference: null,
            containerTypeReference: null,
            carrierReference: null,
            agentReference: null,
            currencyReference: currencyReference
        );

        Assert.AreEqual("USD", record.Currency);
        Assert.AreEqual("Dólar estadounidense", record.CurrencyReference?.Name);
        Assert.AreEqual("USD", record.CurrencyReference?.Code);
    }

    private static PricingExtractionRecord CreateRecord(string? currency)
    {
        return PricingExtractionRecord.Create(
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
            currency,
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
    }
}
