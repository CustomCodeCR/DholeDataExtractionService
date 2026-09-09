using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Workers.Streams;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class PricingExtractionTemplatePolicyTests
{
    [TestMethod]
    public void Apply_SpotRate_UsesCostaRicaTodayAndPersistsEtdAndCommodityInComments()
    {
        var row = new AiPricingEmailRow(
            "Shanghai",
            "Puerto Caldera",
            null,
            "40HC",
            "MSK",
            null,
            "General Cargo",
            "USD",
            7,
            42,
            new DateTime(2026, 9, 1),
            new DateTime(2026, 9, 20),
            8405m,
            null,
            null,
            null,
            8405m,
            null,
            null,
            null,
            null,
            null
        );
        var input = new AiPricingEmailAnalysisResult(
            true,
            Guid.NewGuid(),
            95m,
            [row],
            []
        );

        // 10-Sep UTC is still 09-Sep in Costa Rica. SPOT validity must follow
        // the commercial day in Costa Rica, not the server's UTC date.
        var utcNow = new DateTime(2026, 9, 10, 2, 0, 0, DateTimeKind.Utc);
        var result = PricingExtractionTemplatePolicy.Apply(
            input,
            "TARIFA SPOT MSK",
            "ETD: 6 setiembre 2026\nCommodity: General Cargo",
            null,
            utcNow
        );

        var actual = result.Rows.Single();
        Assert.AreEqual(new DateTime(2026, 9, 9), actual.ValidFrom);
        Assert.AreEqual(new DateTime(2026, 9, 9), actual.ValidTo);
        Assert.IsNotNull(actual.SpaceComment);
        StringAssert.Contains(actual.SpaceComment, "Tipo Tarifa: SPOT");
        StringAssert.Contains(actual.SpaceComment, "ETD: 2026-09-06");
        StringAssert.Contains(actual.SpaceComment, "Commodity: General Cargo");
    }

    [TestMethod]
    public void Apply_NonSpotRate_PreservesPublishedValidity()
    {
        var validFrom = new DateTime(2026, 9, 1);
        var validTo = new DateTime(2026, 9, 20);
        var row = new AiPricingEmailRow(
            "Ningbo",
            "Puerto Caldera",
            null,
            "40HC",
            "MSK",
            null,
            null,
            "USD",
            7,
            42,
            validFrom,
            validTo,
            8405m,
            null,
            null,
            null,
            8405m,
            null,
            null,
            null,
            null,
            null
        );
        var input = new AiPricingEmailAnalysisResult(true, Guid.NewGuid(), 90m, [row], []);

        var result = PricingExtractionTemplatePolicy.Apply(
            input,
            "Monthly contract rates",
            "Valid: 1 Sep - 20 Sep 2026",
            null,
            new DateTime(2026, 9, 9, 18, 0, 0, DateTimeKind.Utc)
        );

        var actual = result.Rows.Single();
        Assert.AreEqual(validFrom, actual.ValidFrom);
        Assert.AreEqual(validTo, actual.ValidTo);
    }
}
