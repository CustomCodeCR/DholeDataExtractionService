using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Infrastructure.Extraction.Email;
using Dhole.DataExtraction.Infrastructure.Normalization;

namespace Dhole.DataExtraction.UnitTests;

/// <summary>
/// Regression contract: Xingang and Tianjin stay distinct, and every +arb amount
/// remains attached exclusively to the POL that published it as an origin charge.
/// </summary>
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

    [TestMethod]
    public async Task RsLogMergedDestinationRows_InheritOnlyPoeWithoutShiftingCarrierOrRates()
    {
        const string body = """
            Please find our updated rates from China Base Ports to WCCA as below.

            POL     POD     CARRIER  20'     40'/40HC        Free time       Effective Date  Expiry date
            Shanghai/Ningbo/Shenzhen/Xiamen/Qingdao     Puerto Quetzal  WHL     $5,808  $6,208  18 days  22-Sep  30-Sep
            Xingang     WHL     $5,958  $6,358  18 days  22-Sep  30-Sep
            Shanghai/Ningbo/Shenzhen/Qingdao/Xingang     PIL     $5,800  $6,200  18 days  22-Sep  30-Sep
            Shanghai/Ningbo/Qingdao     Caldera     OOCL    $5,840  $6,265  18 days  22-Sep  30-Sep
            Xingang/Xiamen     OOCL    $5,890  $6,315  18 days  22-Sep  30-Sep
            Shanghai/Ningbo/Qingdao/Xingang     PIL     $5,800  $6,200  18 days  22-Sep  30-Sep
            Shanghai/Ningbo/Shenzhen/Qingdao/Xingang     Acajutla     PIL     $5,800  $6,200  18 days  22-Sep  30-Sep
            Shanghai/Ningbo/Qingdao/Xingang     Corinto     PIL     $5,800  $6,200  18 days  22-Sep  30-Sep

            General Cargo
            Subject to DTHC and local charges at both ends
            """;

        var document = await new EmailDocumentExtractor().ExtractAsync(
            new DocumentExtractionInput(
                "rslog-rates.txt",
                "text/plain",
                ".txt",
                System.Text.Encoding.UTF8.GetBytes(body)
            )
        );

        var table = document.Tables.Single();
        Assert.AreEqual("EMAIL FCL Matrix", table.SheetName);
        Assert.HasCount(8, table.Rows);

        Assert.AreEqual("Puerto Quetzal", table.Rows[1].Values["POE"]);
        Assert.AreEqual("WHL", table.Rows[1].Values["CARRIER"]);
        Assert.AreEqual("$5,958", table.Rows[1].Values["20GP"]);
        Assert.AreEqual("$6,358", table.Rows[1].Values["40DV/40HC"]);

        Assert.AreEqual("Caldera", table.Rows[4].Values["POE"]);
        Assert.AreEqual("OOCL", table.Rows[4].Values["CARRIER"]);
        Assert.AreEqual("$5,890", table.Rows[4].Values["20GP"]);
        Assert.AreEqual("$6,315", table.Rows[4].Values["40DV/40HC"]);
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
