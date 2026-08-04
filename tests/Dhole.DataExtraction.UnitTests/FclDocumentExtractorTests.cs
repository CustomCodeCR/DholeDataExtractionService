using System.Text;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using ClosedXML.Excel;
using Dhole.DataExtraction.Infrastructure.Extraction.Email;
using Dhole.DataExtraction.Infrastructure.Extraction.Excel;
using Dhole.DataExtraction.Infrastructure.Extraction.Pdf;
using Dhole.DataExtraction.Infrastructure.GrpcClients;
using Dhole.DataExtraction.Infrastructure.Mapping;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class FclDocumentExtractorTests
{
    [TestMethod]
    public void RouteHeaders_MapTariffPodToPoeAndKeepFinalDeliverySeparate()
    {
        Assert.AreEqual(
            "PortOfExit",
            DefaultFclColumnMappings.Mappings["destinationport"]
        );
        Assert.AreEqual(
            "PortOfExit",
            DefaultFclColumnMappings.Mappings["portofdischarge"]
        );
        Assert.AreEqual(
            "PortOfExit",
            DefaultFclColumnMappings.Mappings["pod"]
        );
        Assert.AreEqual(
            "DestinationPort",
            DefaultFclColumnMappings.Mappings["placeofdelivery"]
        );

        Assert.AreEqual(
            "PortOfExit",
            PricingRouteFieldSemantics.ResolveTargetField(
                "destinationport",
                "DestinationPort"
            )
        );
        Assert.AreEqual(
            "PortOfExit",
            PricingRouteFieldSemantics.ResolveTargetField("pod", "DestinationPort")
        );
        Assert.AreEqual(
            "DestinationPort",
            PricingRouteFieldSemantics.ResolveTargetField(
                "finaldestination",
                "PortOfExit"
            )
        );
    }

    [TestMethod]
    public void Combined40StandardAndHighCube_ExpandsToIndependentRows()
    {
        CollectionAssert.AreEqual(
            new[] { "40DV", "40HC" },
            PricingContainerVariants.Expand("40'/40HC").ToArray()
        );
        CollectionAssert.AreEqual(
            new[] { "40DV", "40HC" },
            PricingContainerVariants.Expand("40SV y 40HQ").ToArray()
        );
        CollectionAssert.AreEqual(
            new[] { "40DV", "40HC" },
            PricingContainerVariants.Expand("40DV/HC").ToArray()
        );
        CollectionAssert.AreEqual(
            new[] { "40DV", "40HC" },
            PricingContainerVariants.Expand("40 DV/HC").ToArray()
        );
        CollectionAssert.AreEqual(
            new[] { "40DV", "40HC" },
            PricingContainerVariants.Expand("40GP & HC").ToArray()
        );
        CollectionAssert.AreEqual(
            new[] { "20DV", "40DV", "40HC" },
            PricingContainerVariants.Expand("20' / 40ST / 40HC").ToArray()
        );
    }

    [TestMethod]
    public void ChinaBasePorts_ContainsTheTenCommercialOrigins()
    {
        CollectionAssert.AreEqual(
            new[]
            {
                "Shanghai",
                "Ningbo-Zhoushan",
                "Shenzhen",
                "Qingdao",
                "Guangzhou (Nansha)",
                "Tianjin (Xingang)",
                "Xiamen",
                "Dalian",
                "Lianyungang",
                "Yantian (Shenzhen)",
            },
            PricingBasePorts.China.ToArray()
        );
        Assert.IsTrue(PricingBasePorts.IsChinaOrAsiaBasePorts("asiabaseports"));
    }

    [TestMethod]
    public async Task PlainTextEmail_WithTabSeparatedMatrix_ExtractsAllRowsAndDateColumns()
    {
        const string bodyTemplate = """
            Estimados,

            Compartimos las tarifas vigentes desde China Base Ports hacia Costa Rica:
            POL\tPOD\tNaviera\t20’\t40’/40HC\tFree Time\tVigencia\tExpiración
            China Base Ports\tCaldera\tPIL\tUSD 6,000\tUSD 6,200\t18 días\t1-Jul\t14-Jul
            China Base Ports\tCaldera\tPIL\tUSD 6,400\tUSD 6,600\t18 días\t7-Jul\t14-Jul
            China Base Ports\tCaldera\tOOCL\tUSD 6,190\tUSD 6,465\t18 días\t1-Jul\t7-Jul
            China Base Ports\tColón/Manzanillo\tOOCL\tUSD 7,235\tUSD 7,355\t12 días\t1-Jul\t7-Jul

            Las tarifas se encuentran sujetas a disponibilidad de espacio y equipo.
            """;
        var body = bodyTemplate.Replace("\\t", "\t", StringComparison.Ordinal);

        var extractor = new EmailDocumentExtractor();
        var document = await extractor.ExtractAsync(
            new DocumentExtractionInput(
                "email-body.txt",
                "text/plain",
                ".txt",
                Encoding.UTF8.GetBytes(body)
            )
        );

        var table = document.Tables.Single();
        Assert.HasCount(4, table.Rows);
        CollectionAssert.Contains(table.Headers.ToArray(), "Vigencia");
        CollectionAssert.Contains(table.Headers.ToArray(), "Expiración");
        Assert.AreEqual("China Base Ports", table.Rows.First().Values["POL"]);
        Assert.AreEqual("USD 6,000", table.Rows.First().Values["20’"]);
        Assert.AreEqual("14-Jul", table.Rows.First().Values["Expiración"]);
    }

    [TestMethod]
    public async Task PlainTextEmail_WithOneCellPerLine_ExtractsLatestFakTableOnly()
    {
        const string body = """
            Website: https://logisticacastrofallas.com
            ________________________________________________
            De: Veronica Jiang <veronica.jiang@wwl.sg>
            Asunto: UPDATE FAK WWL / CASTRO FALLAS /31-JULY

            Published FAK for your ref:

            FAK
            POL
            POD
            CARRIER
            Free Time
            Validity (ETD)
            20'GP
            40'GP
            40'HQ

            SHA/NGB/SZN/XMN/TAO/TSN/DLN
            Acajulta/Corinto/Puerto Caldera
            MSC FAK
            21 days dry
            8 Aug-14 Aug
            $6,600
            $7,500
            $7,500

            SHA/NGB/SZN/XMN/TAO/TSN/DLN
            Acajulta/Corinto/Puerto Caldera
            MSC Basket
            21 days dry
            8 Aug-14 Aug
            $6,500
            $7,300
            $7,300

            SHA/NGB/SZN/XMN/TAO
            Acajulta/Corinto/Puerto Caldera
            ONE FAK
            16 days dry
            8 Aug-14 Aug
            $6,900
            $7,200
            $7,200

            SHANGHAI
            Acajulta/Corinto/Puerto Caldera
            PIL
            18 days dry
            7 Aug-14 Aug
            $6,700
            $6,900
            $6,900

            Sub to p/s $50/cntr & MBL RLS $75/BILL

            Published FAK for your ref:
            POL
            POD
            CARRIER
            Free Time
            Validity (ETD)
            20'GP
            40'GP
            40'HQ
            OLD/POL
            OLD/POD
            OLD CARRIER
            1 day
            1 Jan-2 Jan
            $1
            $1
            $1
            """;

        var extractor = new EmailDocumentExtractor();
        var document = await extractor.ExtractAsync(
            new DocumentExtractionInput(
                "email-body.txt",
                "text/plain",
                ".txt",
                Encoding.UTF8.GetBytes(body)
            )
        );

        var table = document.Tables.Single();
        Assert.AreEqual("EMAIL FCL Cell Stream", table.SheetName);
        Assert.HasCount(4, table.Rows);
        var firstRow = table.Rows.First();
        Assert.AreEqual("SHA/NGB/SZN/XMN/TAO/TSN/DLN", firstRow.Values["POL"]);
        Assert.AreEqual("Acajulta/Corinto/Puerto Caldera", firstRow.Values["POE"]);
        Assert.AreEqual("MSC FAK", firstRow.Values["Carrier"]);
        Assert.AreEqual("8 Aug", firstRow.Values["ValidFrom"]);
        Assert.AreEqual("14 Aug", firstRow.Values["ValidTo"]);
        Assert.AreEqual("$6,600", firstRow.Values["20GP"]);
        Assert.AreEqual("$7,500", firstRow.Values["40HQ"]);
        StringAssert.Contains(firstRow.Values["Remarks"], "Producto comercial: FAK");
        StringAssert.Contains(firstRow.Values["Remarks"], "p/s $50/cntr");
        StringAssert.Contains(firstRow.Values["Remarks"], "MBL RLS $75/BILL");
        Assert.IsTrue(table.Rows.All(row =>
            row.Values.TryGetValue("Remarks", out var remarks)
            && remarks?.Contains("p/s $50/cntr", StringComparison.OrdinalIgnoreCase) == true
        ));
    }

    [TestMethod]
    public async Task HtmlLikePipeMatrix_WithFakTitle_DoesNotShiftColumns()
    {
        const string body = """
            |FAK|POL|POD|CARRIER|Free Time|Validity (ETD)|20'GP|40'GP|40'HQ|
            |SHA/NGB|Acajulta/Puerto Caldera|MSC FAK|21 days dry|8 Aug-14 Aug|$6,600|$7,500|$7,500|
            Sub to p/s $50/cntr
            """;

        var extractor = new EmailDocumentExtractor();
        var document = await extractor.ExtractAsync(
            new DocumentExtractionInput(
                "email-body.html",
                "text/html",
                ".html",
                Encoding.UTF8.GetBytes(body)
            )
        );

        var table = document.Tables.Single();
        var row = table.Rows.Single();
        Assert.AreEqual("EMAIL FCL Matrix", table.SheetName);
        Assert.AreEqual("SHA/NGB", row.Values["POL"]);
        Assert.AreEqual("Acajulta/Puerto Caldera", row.Values["POE"]);
        Assert.AreEqual("MSC FAK", row.Values["Carrier"]);
        Assert.AreEqual("8 Aug", row.Values["ValidFrom"]);
        Assert.AreEqual("14 Aug", row.Values["ValidTo"]);
        Assert.AreEqual("$6,600", row.Values["20GP"]);
        StringAssert.Contains(row.Values["Remarks"], "Producto comercial: FAK");
        StringAssert.Contains(row.Values["Remarks"], "Sub to p/s $50/cntr");
    }

    [TestMethod]
    public async Task NarrativeNacEmail_ExtractsNewestOfferWithoutCallingAi()
    {
        const string body = """
            De: Veronica.jiang <veronica.jiang@wwl.sg>
            Asunto: CASTRO FALLS// WWL CONTRACT ONE-MSC / AUG

            Dear Royner,

            Pls consider rate USD6300/6400 , valid 8-14/Aug Carrier MSC/ONE NAC with 21 days free at dest, subject to space (except TIANJIN/XIAMEN)
            Subject to isps $15/cntr, p/s $50/cntr, MBL RLS at dest. $75/BL.

            Below the details of ONE NAC:

            A)
            POL: Shanghai/Kaohsiung/Shekou/Qingdao/Ningbo
            POD: Acajutla/Corinto/Caldera
            COMM: Auto Spare Parts

            B)
            POL: Shanghai/Ningbo/Shekou/Yantian/Qingdao/Xiamen/Tianjin(+ arb USD100)/Nanjing(+arb USD400)/Wuhan(+arb USD450)/Chongqing(+arb USD850)
            POD: Acajutla/Corinto/Caldera
            COMM: RETAIL (shoes/furniture/toys)

            C)
            POL: Shanghai/Yantian/Qingdao/Ningbo
            POD: Acajutla/Corinto/Caldera
            COMM: Solar Panels/Solar Modules/LED Lights

            ________________________________________________
            Un saludo cordial

            发件人: Veronica.jiang
            Pls consider rate ONE USD5815 per 40HC, MSC USD6050 per 40HC, valid 1-7/Aug with 21 days free at dest
            """;

        var extractor = new EmailDocumentExtractor();
        var document = await extractor.ExtractAsync(
            new DocumentExtractionInput(
                "email-body.txt",
                "text/plain",
                ".txt",
                Encoding.UTF8.GetBytes(body)
            )
        );

        var table = document.Tables.Single();
        Assert.AreEqual("EMAIL NAC Narrative", table.SheetName);
        Assert.HasCount(8, table.Rows);

        var msc = table.Rows.Single(row => row.Values["Carrier"] == "MSC");
        Assert.AreEqual("6300", msc.Values["FreightAmount"]);
        Assert.AreEqual("40HC", msc.Values["ContainerSize"]);
        Assert.AreEqual("8 Aug", msc.Values["ValidFrom"]);
        Assert.AreEqual("14 Aug", msc.Values["ValidTo"]);
        Assert.AreEqual("21", msc.Values["FreeDays"]);
        Assert.IsFalse(msc.Values["POL"]!.Contains("Tianjin", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(msc.Values["POL"]!.Contains("Xiamen", StringComparison.OrdinalIgnoreCase));

        var oneRows = table.Rows.Where(row => row.Values["Carrier"] == "ONE").ToArray();
        Assert.HasCount(7, oneRows);
        Assert.IsTrue(oneRows.All(row => row.Values["FreightAmount"] == "6400"));
        Assert.IsTrue(oneRows.Any(row => row.Values["Commodity"] == "Auto Spare Parts"));
        Assert.IsTrue(oneRows.All(row => row.Values["POE"] == "Acajutla/Corinto/Caldera"));
        Assert.IsTrue(oneRows.Any(row => row.Values["POL"]!.Contains("Xiamen", StringComparison.OrdinalIgnoreCase)));

        var tianjin = oneRows.Single(row => row.Values["POL"] == "Tianjin");
        Assert.AreEqual("100", tianjin.Values["OriginCharges"]);
        Assert.AreEqual("65", tianjin.Values["Surcharges"]);
        Assert.IsTrue(tianjin.Values["Remarks"]!.Contains("USD 100", StringComparison.OrdinalIgnoreCase));

        Assert.AreEqual("400", oneRows.Single(row => row.Values["POL"] == "Nanjing").Values["OriginCharges"]);
        Assert.AreEqual("450", oneRows.Single(row => row.Values["POL"] == "Wuhan").Values["OriginCharges"]);
        Assert.AreEqual("850", oneRows.Single(row => row.Values["POL"] == "Chongqing").Values["OriginCharges"]);
        Assert.IsFalse(table.Rows.Any(row => row.Values["FreightAmount"] == "5815"));
    }


    [TestMethod]
    public async Task NarrativeNacOutlookHtml_ReconstructsWrappedCurrentOfferAndIgnoresHistory()
    {
        const string body = """
            <html><body>
            <div>Firma y aviso legal de Castro Fallas</div>
            <div>De: Veronica.jiang &lt;veronica.jiang@wwl.sg&gt;</div>
            <div>Asunto: CASTRO FALLS// WWL CONTRACT ONE-MSC / AUG</div>
            <div>Pls consider rate&nbsp;&nbsp;&nbsp; USD6300/6400</div>
            <div>,</div>
            <div>valid 8-14/Aug&nbsp;&nbsp; Carrier MSC/ONE NAC with 21 days free at dest, subject to space</div>
            <div>(except TIANJIN/XIAMEN)</div>
            <div>If big lot, case by case.</div>
            <div>Subject to isps $15/cntr, p/s $50/cntr, MBL RLS at dest. $75/BL.</div>
            <div>Below the details of ONE NAC:</div>
            <div>Pls note, ONE NAC must match COMM as I listed below</div>
            <div>A)</div>
            <div>POL: Shanghai/Kaohsiung/Shekou/Qingdao/Ningbo</div>
            <div>POD: Acajutla/Corinto/Caldera</div>
            <div>COMM: Auto Spare Parts</div>
            <div>B)</div>
            <div>POL: Shanghai/Ningbo/Shekou/Yantian/Qingdao/Xiamen/Tianjin(+ arb USD100)/Nanjing(+arb</div>
            <div>USD400)/Wuhan(+arb USD450)/Chongqing(+arb USD850)</div>
            <div>POD: Acajutla/Corinto/Caldera</div>
            <div>COMM: RETAIL (shoes/furniture/toys/Baby Goods/plastics/apparel &amp; clothing/mattress/diaper</div>
            <div>/bicycle/home appliance/electronic goods/paper/Lights / Stationary/wet wipes/aluminium profile/ glass/plywood)</div>
            <div>C)</div>
            <div>POL: Shanghai/Yantian/Qingdao/Ningbo</div>
            <div>POD: Acajutla/Corinto/Caldera</div>
            <div>COMM: Solar Panels/Solar Modules/LED Lights</div>
            <div>Un saludo cordial</div>
            <div>Veronica Jiang</div>
            <div>发件人: Veronica.jiang</div>
            <div>Pls consider rate ONE USD5815 per 40HC, MSC USD6050 per 40HC, valid 1-7/Aug with 21 days free at dest</div>
            </body></html>
            """;

        var extractor = new EmailDocumentExtractor();
        var document = await extractor.ExtractAsync(
            new DocumentExtractionInput(
                "email-body.html",
                "text/html",
                ".html",
                Encoding.UTF8.GetBytes(body)
            )
        );

        var table = document.Tables.Single();
        Assert.AreEqual("EMAIL NAC Narrative", table.SheetName);
        Assert.HasCount(8, table.Rows);
        Assert.IsFalse(table.Rows.Any(row => row.Values["FreightAmount"] == "5815"));

        var msc = table.Rows.Single(row => row.Values["Carrier"] == "MSC");
        Assert.AreEqual("6300", msc.Values["FreightAmount"]);
        Assert.AreEqual("8 Aug", msc.Values["ValidFrom"]);
        Assert.AreEqual("14 Aug", msc.Values["ValidTo"]);
        Assert.AreEqual("40HC", msc.Values["ContainerSize"]);

        var oneRows = table.Rows.Where(row => row.Values["Carrier"] == "ONE").ToArray();
        Assert.HasCount(7, oneRows);
        Assert.AreEqual("400", oneRows.Single(row => row.Values["POL"] == "Nanjing").Values["OriginCharges"]);
        Assert.AreEqual("850", oneRows.Single(row => row.Values["POL"] == "Chongqing").Values["OriginCharges"]);
        Assert.IsTrue(oneRows.Any(row => row.Values["Commodity"]!.Contains("bicycle", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void CarrierAndPortAliases_NormalizeFreightEmailValues()
    {
        Assert.AreEqual("MSC", Dhole.DataExtraction.Infrastructure.Normalization.CarrierNameNormalizer.Normalize("MSC FAK"));
        Assert.AreEqual("MSC", Dhole.DataExtraction.Infrastructure.Normalization.CarrierNameNormalizer.Normalize("MSC Basket"));
        Assert.AreEqual("ONE", Dhole.DataExtraction.Infrastructure.Normalization.CarrierNameNormalizer.Normalize("ONE FAK"));
        Assert.AreEqual("SHENZHEN", Dhole.DataExtraction.Infrastructure.Normalization.PortNameNormalizer.Normalize("SZN"));
        Assert.AreEqual("XIAMEN", Dhole.DataExtraction.Infrastructure.Normalization.PortNameNormalizer.Normalize("XMN"));
        Assert.AreEqual("TIANJIN (XINGANG)", Dhole.DataExtraction.Infrastructure.Normalization.PortNameNormalizer.Normalize("TSN"));
        Assert.AreEqual("DALIAN", Dhole.DataExtraction.Infrastructure.Normalization.PortNameNormalizer.Normalize("DLN"));
        Assert.AreEqual("ACAJUTLA", Dhole.DataExtraction.Infrastructure.Normalization.PortNameNormalizer.Normalize("Acajulta"));
    }


    [TestMethod]
    public async Task Excel_DiamondTierCalderaMatrix_EnrichesRowsWithRouteCarrierAndValidity()
    {
        using var workbook = new XLWorkbook();
        var rates = workbook.AddWorksheet("Tarifas DT Via Caldera");
        rates.Cell("A2").Value = "Validez";
        rates.Cell("A3").Value = "DT CALDERA del 08 al 14 de AGOSTO";
        rates.Cell("A6").Value = "POL";
        rates.Cell("B6").Value = "20 DV";
        rates.Cell("C6").Value = "40 DV/HC";
        rates.Cell("F6").Value = "POL Additional TAO";
        rates.Cell("I6").Value = "20 DV";
        rates.Cell("J6").Value = "40DV/HC";
        rates.Cell("A7").Value = "Shekou";
        rates.Cell("B7").Value = 8500;
        rates.Cell("C7").Value = 10200;
        rates.Cell("F7").Value = "MAKASSAR";
        rates.Cell("I7").Value = 330;
        rates.Cell("J7").Value = 600;
        rates.Cell("A8").Value = "Shanghai";
        rates.Cell("B8").Value = 8500;
        rates.Cell("C8").Value = 10200;
        rates.Cell("A10").Value = "Información importante a considerar";

        var quote = workbook.AddWorksheet("Cotizador - DT Via Caldera");
        quote.Cell("B6").Value = "Para MSC es un gusto saludarle";
        quote.Cell("B7").Value = "Nuestra oferta DT CALDERA del 08 al 14 de AGOSTO";

        await using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var extractor = new ExcelDocumentExtractor();
        var document = await extractor.ExtractAsync(
            new DocumentExtractionInput(
                "MSC DT CALDERA - Validez 08 al 14 de AGOSTO.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".xlsx",
                stream.ToArray()
            )
        );

        var table = document.Tables.Single();
        Assert.Contains("FCL normalizado", table.SheetName!, StringComparison.Ordinal);
        Assert.HasCount(2, table.Rows);

        var first = table.Rows.First();
        Assert.AreEqual("Shekou", first.Values["POL"]);
        Assert.AreEqual("Puerto Caldera", first.Values["POE"]);
        Assert.AreEqual("MSC", first.Values["Carrier"]);
        Assert.AreEqual("USD", first.Values["Currency"]);
        Assert.AreEqual("8500", first.Values["20 DV"]);
        Assert.AreEqual("10200", first.Values["40 DV/HC"]);
        Assert.AreEqual("Diamond Tier", first.Values["RouteMode"]);
        Assert.IsTrue(first.Values["ValidFrom"]!.EndsWith("-08-08", StringComparison.Ordinal));
        Assert.IsTrue(first.Values["ValidTo"]!.EndsWith("-08-14", StringComparison.Ordinal));
        Assert.IsFalse(table.Headers.Contains("POL Additional TAO", StringComparer.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Pdf_WithAlignedFclMatrix_ExtractsRowsFreeDaysAndValidity()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "tarifas_china_base_ports.pdf"
        );

        var extractor = new PdfDocumentExtractor();
        var document = await extractor.ExtractAsync(
            new DocumentExtractionInput(
                "tarifas_china_base_ports.pdf",
                "application/pdf",
                ".pdf",
                await File.ReadAllBytesAsync(fixturePath)
            )
        );

        var table = document.Tables.Single();
        Assert.AreEqual("PDF FCL Aligned Matrix", table.SheetName);
        Assert.HasCount(4, table.Rows);
        CollectionAssert.Contains(table.Headers.ToArray(), "Free Time");
        CollectionAssert.Contains(table.Headers.ToArray(), "Effective");
        CollectionAssert.Contains(table.Headers.ToArray(), "Expiry");
        Assert.AreEqual("18 días", table.Rows.First().Values["Free Time"]);
        Assert.AreEqual("15-Jul-2026", table.Rows.First().Values["Effective"]);
        Assert.AreEqual("31-Jul-2026", table.Rows.First().Values["Expiry"]);
    }


    [TestMethod]
    public async Task EmlTextForAi_SelectsNewestPricingMessageInsteadOfQuotedHistory()
    {
        const string eml = """
            From: Sonia Quiros <squiros@castrofallas.com>
            To: extraccion@example.com
            Subject: RV: CASTRO FALLS// WWL CONTRACT ONE-MSC / AUG
            MIME-Version: 1.0
            Content-Type: text/plain; charset=utf-8

            AVISO LEGAL: contenido confidencial
            De: Veronica.jiang <veronica.jiang@wwl.sg>
            Pls consider rate USD6300/6400 , valid 8-14/Aug Carrier MSC/ONE NAC with 21 days free at dest
            Subject to isps $15/cntr, p/s $50/cntr, MBL RLS at dest. $75/BL.
            A)
            POL: Shanghai/Kaohsiung/Shekou/Qingdao/Ningbo
            POD: Acajutla/Corinto/Caldera
            COMM: Auto Spare Parts
            Un saludo cordial
            Veronica Jiang
            发件人: Veronica.jiang
            Pls consider rate ONE USD5815 per 40HC, MSC USD6050 per 40HC, valid 1-7/Aug with 21 days free at dest
            """;
        var reader = new AiEmailContentReader(
            new ConfigurationBuilder().Build(),
            NullLogger<AiEmailContentReader>.Instance
        );

        var content = Encoding.UTF8.GetBytes(eml);
        var text = await reader.ReadAsTextAsync(
            "thread.eml",
            "message/rfc822",
            ".eml",
            content
        );

        StringAssert.Contains(text, "USD6300/6400");
        StringAssert.Contains(text, "8-14/Aug");
        Assert.IsFalse(text.Contains("USD5815", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(text.Contains("AVISO LEGAL", StringComparison.OrdinalIgnoreCase));

        var document = await new EmailDocumentExtractor().ExtractAsync(
            new DocumentExtractionInput(
                "thread.eml",
                "message/rfc822",
                ".eml",
                content
            )
        );
        var table = document.Tables.Single();
        Assert.AreEqual("EMAIL NAC Narrative", table.SheetName);
        Assert.HasCount(2, table.Rows);
        Assert.IsTrue(table.Rows.Any(row => row.Values["FreightAmount"] == "6300"));
        Assert.IsTrue(table.Rows.Any(row => row.Values["FreightAmount"] == "6400"));
        Assert.IsFalse(table.Rows.Any(row => row.Values["FreightAmount"] == "5815"));
    }

    [TestMethod]
    public async Task PdfTextForAi_PreservesVisualRowsAndContainerColumns()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "tarifas_china_base_ports.pdf"
        );
        var reader = new AiEmailContentReader(
            new ConfigurationBuilder().Build(),
            NullLogger<AiEmailContentReader>.Instance
        );

        var text = await reader.ReadAsTextAsync(
            "tarifas_china_base_ports.pdf",
            "application/pdf",
            ".pdf",
            await File.ReadAllBytesAsync(fixturePath)
        );

        Assert.Contains("40'/40HC", text, StringComparison.Ordinal);
        Assert.Contains("China Base Ports", text, StringComparison.Ordinal);
        Assert.Contains("USD", text, StringComparison.Ordinal);
        Assert.IsGreaterThan(3, text.Split('\n').Length);
    }
}
