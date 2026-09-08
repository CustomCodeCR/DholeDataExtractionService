using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Infrastructure.Extraction.Email;
using Dhole.DataExtraction.Infrastructure.Normalization;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class CarrierEmailPricingIntegrityRegressionTests_OriginArbitraries
{
    [TestMethod]
    public void Xingang_IsNeverNormalizedAsTianjin()
    {
        Assert.AreEqual("XINGANG", PortNameNormalizer.Normalize("Xingang"));
        Assert.AreEqual("XINGANG", PortNameNormalizer.Normalize("Xingang Port"));
        Assert.AreEqual("TIANJIN", PortNameNormalizer.Normalize("Tianjin"));
        Assert.AreEqual("TIANJIN", PortNameNormalizer.Normalize("Tianjin Port"));
        Assert.AreNotEqual(
            PortNameNormalizer.Normalize("Xingang"),
            PortNameNormalizer.Normalize("Tianjin")
        );
    }

    [TestMethod]
    public void WwlOneNac_PreservesWrappedPerPortArbitrariesWithOrWithoutSpaces()
    {
        const string body = """
            Dear Royner,

            Pls consider rate USD7300 per 40HC valid 15-21/Sep Carrier MSC/ONE NAC with 21 days free at dest, subject to space
            Below the details of ONE NAC:
            Pls note, ONE NAC must match COMM as I listed below

            B)
            POL:Shanghai/Ningbo/Shekou/Yantian/Qingdao/Xiamen/Tianjin(+arbUSD100)/Nanjing(+arb USD400)/Wuhan(+arb USD450)/
            Chongqing(+arb USD850)
            POD: Acajutla/Corinto/Caldera
            COMM: RETAIL (shoes/furniture/toys/Baby Goods/plastics/apparel & clothing)

            Regards
            """;

        var table = EmailDocumentExtractor.TryExtractNarrativeNacTable(body);

        Assert.IsNotNull(table);
        var oneRows = table.Rows
            .Where(row => string.Equals(row.Values["Carrier"], "ONE", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.IsTrue(oneRows.Length >= 5);
        AssertArbitrary(oneRows, "Tianjin", "100");
        AssertArbitrary(oneRows, "Nanjing", "400");
        AssertArbitrary(oneRows, "Wuhan", "450");
        AssertArbitrary(oneRows, "Chongqing", "850");

        var regular = oneRows.Single(row =>
            row.Values["POL"]?.Contains("Shanghai", StringComparison.OrdinalIgnoreCase) == true
        );
        Assert.IsTrue(string.IsNullOrWhiteSpace(regular.Values["OriginCharges"]));
        Assert.IsFalse(oneRows.Any(row =>
            row.Values["POL"]?.Contains("Tianjin(", StringComparison.OrdinalIgnoreCase) == true
            || row.Values["POL"]?.Contains("arb", StringComparison.OrdinalIgnoreCase) == true
        ));
    }

    private static void AssertArbitrary(
        IReadOnlyCollection<ExtractedRow> rows,
        string port,
        string expectedAmount
    )
    {
        var row = rows.Single(item =>
            string.Equals(item.Values["POL"], port, StringComparison.OrdinalIgnoreCase)
        );
        Assert.AreEqual(expectedAmount, row.Values["OriginCharges"]);
    }
}
