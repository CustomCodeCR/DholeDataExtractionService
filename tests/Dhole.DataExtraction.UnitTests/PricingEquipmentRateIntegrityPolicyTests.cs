using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Workers.Streams;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class PricingEquipmentRateIntegrityPolicyTests
{
    [TestMethod]
    public void Reconcile_DoesNotCopy20DvFreightInto40DvOr40Hc()
    {
        const string source = """
            POL: Shanghai
            POE: Caldera
            Validity: 2026-09-01 - 2026-09-30
            20DV: USD 1250
            40DV: USD 2100
            40HC: USD 2250
            """;

        var rows = new[]
        {
            Row("20DV", 1250m),
            Row("40DV", 1250m),
            Row("40HC", 1250m),
        };

        var reconciled = PricingEquipmentRateIntegrityPolicy.Reconcile(
            rows,
            "Ocean rates",
            source,
            source,
            out var corrections
        );

        Assert.AreEqual(2, corrections);
        Assert.AreEqual(1250m, reconciled.Single(row => row.ContainerType == "20DV").OceanFreight);
        Assert.AreEqual(2100m, reconciled.Single(row => row.ContainerType == "40DV").OceanFreight);
        Assert.AreEqual(2250m, reconciled.Single(row => row.ContainerType == "40HC").OceanFreight);
    }

    [TestMethod]
    public void Reconcile_UsesColumnPositionForEquipmentMatrix()
    {
        const string source = """
            POL | POE | 20DV | 40DV | 40HC
            Shanghai | Caldera | 1300 | 2200 | 2350
            """;

        var rows = new[]
        {
            Row("20GP", 1300m),
            Row("40GP", 1300m),
            Row("40HQ", 1300m),
        };

        var reconciled = PricingEquipmentRateIntegrityPolicy.Reconcile(
            rows,
            null,
            source,
            source,
            out var corrections
        );

        Assert.AreEqual(2, corrections);
        Assert.AreEqual(1300m, reconciled[0].OceanFreight);
        Assert.AreEqual(2200m, reconciled[1].OceanFreight);
        Assert.AreEqual(2350m, reconciled[2].OceanFreight);
    }

    [TestMethod]
    public void Reconcile_DoesNotGuessWhenSameEquipmentHasSeveralFreights()
    {
        const string source = """
            Shanghai - Caldera - 40HC: USD 2100
            Ningbo - Caldera - 40HC: USD 2300
            """;

        var rows = new[] { Row("40HC", 2200m) };

        var reconciled = PricingEquipmentRateIntegrityPolicy.Reconcile(
            rows,
            null,
            source,
            source,
            out var corrections
        );

        Assert.AreEqual(0, corrections);
        Assert.AreEqual(2200m, reconciled[0].OceanFreight);
    }

    private static AiPricingEmailRow Row(string equipment, decimal freight) =>
        new(
            "Shanghai",
            "Caldera",
            null,
            equipment,
            "MSC",
            null,
            null,
            "USD",
            null,
            null,
            new DateTime(2026, 9, 1),
            new DateTime(2026, 9, 30),
            freight,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );
}
