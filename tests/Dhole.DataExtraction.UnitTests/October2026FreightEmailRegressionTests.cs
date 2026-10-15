using System.Text;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Infrastructure.Email;
using Dhole.DataExtraction.Infrastructure.Extraction.Email;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class October2026FreightEmailRegressionTests
{
    [TestMethod]
    public async Task RslOctoberForwardedStackedTable_ExtractsTwoDestinationsAndCurrentRatesOnly()
    {
        const string body = """
            Website: https://logisticacastrofallas.com
            AVISO LEGAL: Este mensaje es confidencial.
            ________________________________________________
            De: RSL - Sia Liu (SH) <sialiu.sh@rslog.com>
            Enviado: jueves, 15 de octubre de 2026 01:27
            Asunto: Updated Ocean Freight Rates - China to Costa Rica & Guatemala / RSL
            Dear Royner,
            Pls check below update rate for your ref.
            POL
            POD
            CARRIER
            20'
            40'/40HC
            Free time
            Effective Date
            Expiry date
            Shanghai/ Ningbo/ Qingdao
            Caldera
            OOCL
            $6,340
            $6,665
            18 days
            15-Oct
            21-Oct
            China Base Ports
            Moin
            ONE
            $9,015
            $9,115
            12 days
            15-Oct
            21-Oct
            Shanghai/ Ningbo/ Shenzhen/ Xiamen/ Qingdao
            Puerto Quetzal
            WHL
            $6,508
            $6,708
            18 days
            15-Oct
            21-Oct
            General Cargo
            Subject to DTHC and local charges at both ends
            Please consider ONE overweight surcharge: 18-21 tons - USD 200/20'
            Best regards,
            Sia Liu
            发件人: Royner Sibaja
            Please share the rates from China to Caldera.
            """;

        var focused = EmailPricingContentSelector.SelectPreferredBody(body, null);
        Assert.IsFalse(focused.Contains("AVISO LEGAL", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(focused.Contains("Please share the rates from China", StringComparison.OrdinalIgnoreCase));

        var result = await new EmailDocumentExtractor().ExtractAsync(new DocumentExtractionInput(
            "october-rsl.txt", "text/plain", ".txt", Encoding.UTF8.GetBytes(focused)
        ));

        var table = result.Tables.Single();
        Assert.AreEqual("EMAIL FCL Cell Stream", table.SheetName);
        Assert.HasCount(3, table.Rows);
        Assert.AreEqual("Caldera", table.Rows.First().Values["POE"]);
        Assert.AreEqual("OOCL", table.Rows.First().Values["Carrier"]);
        Assert.IsTrue(table.Rows.First().Values.Values.Contains("$6,665"));
        Assert.AreEqual("Moin", table.Rows.ElementAt(1).Values["POE"]);
        Assert.AreEqual("Puerto Quetzal", table.Rows.Last().Values["POE"]);
        Assert.AreEqual("15-Oct", table.Rows.First().Values["ValidFrom"]);
        Assert.AreEqual("21-Oct", table.Rows.First().Values["ValidTo"]);
    }

    [TestMethod]
    public async Task WwlOctoberNarrative_UsesLatestOfferAndRejectsExcludedOrigins()
    {
        const string body = """
            Website: https://logisticacastrofallas.com
            AVISO LEGAL: Este mensaje es confidencial.
            ________________________________________________
            De: Veronica.jiang <veronica.jiang@wwl.sg>
            Enviado: jueves, 15 de octubre de 2026 03:55
            Asunto: WWL CONTRACT ONE-MSC / OCT
            Dear Royner,
            Pls consider rate USD7200 per 40HC valid 22/Oct-31/Oct Carrier MSC/ONE NAC with 21 days free at dest, subject to space (except TIANJIN/XIAMEN)
            Subject to isps $15/cntr, p/s $50/cntr, MBL RLS at dest. $75/BL.
            Below the details of ONE NAC:
            A)
            POL: Shanghai/Shekou/Qingdao/Ningbo
            POD: Acajutla/Corinto/Caldera
            COMM: Auto Spare Parts
            B)
            POL: Shanghai/Ningbo/Shekou/Yantian/Qingdao/Xiamen/Tianjin(+arbUSD100)/Nanjing(+arb USD400)/Wuhan(+arb USD450)/Chongqing(+arb USD850)
            POD: Acajutla/Corinto/Caldera
            COMM: RETAIL (shoes/furniture/toys)
            C)
            POL: Shanghai/Yantian/Qingdao/Ningbo
            POD: Acajutla/Corinto/Caldera
            COMM: Solar Panels/Solar Modules/LED Lights
            Un saludo cordial
            Veronica Jiang
            发件人: Veronica.jiang
            发送时间: 2026年10月9日 16:38
            Pls consider rate USD6200 per 40HC valid 15/Oct-21/Oct Carrier MSC/ONE NAC with 21 days free at dest
            """;

        var focused = EmailPricingContentSelector.SelectPreferredBody(body, null);
        StringAssert.Contains(focused, "USD7200");
        Assert.IsFalse(focused.Contains("USD6200", StringComparison.Ordinal));

        var document = await new EmailDocumentExtractor().ExtractAsync(new DocumentExtractionInput(
            "october-wwl.txt", "text/plain", ".txt", Encoding.UTF8.GetBytes(focused)
        ));
        var table = document.Tables.Single();
        Assert.AreEqual("EMAIL NAC Narrative", table.SheetName);
        Assert.IsTrue(table.Rows.Count > 0);
        Assert.IsTrue(table.Rows.All(row => row.Values["FreightAmount"] == "7200"));
        Assert.IsTrue(table.Rows.All(row => row.Values["ContainerSize"] == "40HC"));
        Assert.IsTrue(table.Rows.All(row => row.Values["FreeDays"] == "21"));
        Assert.IsTrue(table.Rows.All(row => !row.Values["POL"]!.Contains("Tianjin", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(table.Rows.All(row => !row.Values["POL"]!.Contains("Xiamen", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(table.Rows.Any(row => row.Values["Carrier"] == "ONE" && row.Values["OriginCharges"] == "850"));
        Assert.IsTrue(table.Rows.All(row => row.Values["ValidFrom"]!.Contains("22", StringComparison.Ordinal)));
        Assert.IsTrue(table.Rows.All(row => row.Values["ValidTo"]!.Contains("31", StringComparison.Ordinal)));
    }
}
