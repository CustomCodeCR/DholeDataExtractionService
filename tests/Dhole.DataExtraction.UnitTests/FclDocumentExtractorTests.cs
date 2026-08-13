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
    public async Task StackedMatrix_WithSeparateEffectiveAndExpiryDates_ExtractsBothValidityBoundaries()
    {
        const string body = """
            Dear Marco,

            Pls check below update rates for your ref.

            POL
            POD
            CARRIER
            20'
            40'/40HC
            Free time
            Effective Date
            Expiry date

            Shanghai/ Ningbo
            Caldera
            PIL
            $6,560
            $6,830
            18 days
            8-Aug
            14-Aug

            Xingang/Qingdao
            Caldera
            PIL
            $7,100
            $7,400
            18 days
            8-Aug
            14-Aug

            General Cargo
            Subject to DTHC and local charges at both ends
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
        Assert.HasCount(2, table.Rows);
        Assert.IsTrue(table.Rows.All(row => row.Values["ValidFrom"] == "8-Aug"));
        Assert.IsTrue(table.Rows.All(row => row.Values["ValidTo"] == "14-Aug"));
        Assert.AreEqual("$6,560", table.Rows.First().Values["20GP"]);
        Assert.AreEqual("$6,830", table.Rows.First().Values["40DV/40HC"]);

        var mappedRows = await new ColumnMappingService(null!).MapAsync(document);
        Assert.IsTrue(mappedRows.All(row => row.Values["ValidFrom"] == "8-Aug"));
        Assert.IsTrue(mappedRows.All(row => row.Values["ValidTo"] == "14-Aug"));
        Assert.IsTrue(mappedRows.Any(row => row.Values["ContainerType"] == "40DV"));
        Assert.IsTrue(mappedRows.Any(row => row.Values["ContainerType"] == "40HC"));

        var records = await new Dhole.DataExtraction.Infrastructure.Normalization.PricingRecordNormalizer()
            .NormalizeAsync(Guid.NewGuid(), Guid.NewGuid(), mappedRows);
        Assert.IsTrue(records.All(record => record.ValidFrom.HasValue));
        Assert.IsTrue(records.All(record => record.ValidTo.HasValue));
        Assert.IsTrue(records.All(record => record.ValidFrom!.Value.Month == 8 && record.ValidFrom.Value.Day == 8));
        Assert.IsTrue(records.All(record => record.ValidTo!.Value.Month == 8 && record.ValidTo.Value.Day == 14));
    }

    [TestMethod]
    public async Task RsRatesUpdate_WithEffectiveEtdAndSecondMatrix_ExtractsBothTablesAndSkipsOmittedSailing()
    {
        const string body = """
            Dear all,

            Currently, space is tight, since vessel schedules are very unstable due to frequent rollovers.
            Please find below the rates for your reference. However, space availability needs to be confirmed on a case-by-case basis locally.
            For Central America, we expect a further increase of approximately USD 1,000 per container in the last week of this month.

            POL
            POD
            CARRIER
            20'
            40'/40HC
            Free time
            Effective ETD

            Shanghai
            Caldera
            PIL
            $7,600
            $7,800
            18 days
            24-Aug

            Ningbo
            Caldera
            PIL
            $7,600
            $7,800
            18 days
            21-Aug

            Qingdao
            Caldera
            PIL
            $7,900
            $8,100
            18 days
            18-Aug

            Xingang
            Caldera
            PIL
            $7,900
            $8,100
            18 days
            OMIT

            POL
            POD
            CARRIER
            20'
            40'/40HC
            Free time
            Effective Date
            Expiry date

            China Base Ports
            Caldera
            MSC
            $7,415
            $8,315
            14 days
            15-Aug
            21-Aug

            China Base Ports
            Rodman
            MSC
            $6,215
            $7,015
            14 days
            15-Aug
            21-Aug

            China Base Ports
            Cristobal/Colon
            MSC
            $7,565
            $7,915
            14 days
            15-Aug
            21-Aug

            China Base Ports
            Manzanillo
            ONE
            $6,975
            $7,075
            12 days
            15-Aug
            21-Aug

            China Base Ports
            Moin
            ONE
            $7,815
            $7,915
            12 days
            15-Aug
            21-Aug

            China Base Ports
            Moin
            MSC
            $8,565
            $8,715
            14 days
            15-Aug
            21-Aug

            General Cargo
            Subject to DTHC and local charges at both ends
            Please consider ONE overweight surcharge: 18-21 tons - USD 200/20'
            """;

        var extractor = new EmailDocumentExtractor();
        var document = await extractor.ExtractAsync(
            new DocumentExtractionInput(
                "rs-rates-update.txt",
                "text/plain",
                ".txt",
                Encoding.UTF8.GetBytes(body)
            )
        );

        Assert.HasCount(2, document.Tables);
        Assert.HasCount(4, document.Tables.First().Rows);
        Assert.HasCount(6, document.Tables.Last().Rows);
        Assert.AreEqual(
            "24-Aug",
            document.Tables.First().Rows.First().Values["Effective ETD"]
        );

        var mappedRows = await new ColumnMappingService(null!).MapAsync(document);

        Assert.IsFalse(mappedRows.Any(row =>
            row.Values.TryGetValue("OriginPort", out var pol)
            && string.Equals(pol, "Xingang", StringComparison.OrdinalIgnoreCase)
            && row.Values.TryGetValue("Carrier", out var carrier)
            && string.Equals(carrier, "PIL", StringComparison.OrdinalIgnoreCase)
        ));

        var pilRows = mappedRows
            .Where(row => row.Values.TryGetValue("Carrier", out var carrier)
                && string.Equals(carrier, "PIL", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.HasCount(9, pilRows);
        Assert.IsTrue(pilRows.All(row =>
            row.Values.TryGetValue("ValidFrom", out var validFrom)
            && row.Values.TryGetValue("ValidTo", out var validTo)
            && string.Equals(validFrom, validTo, StringComparison.OrdinalIgnoreCase)
        ));
        Assert.IsTrue(pilRows.All(row =>
            row.Values.TryGetValue("Remarks", out var remarks)
            && remarks is not null
            && remarks.Contains("Effective ETD", StringComparison.OrdinalIgnoreCase)
        ));
        Assert.IsTrue(mappedRows.All(row =>
            row.Values.TryGetValue("SpaceComment", out var spaceComment)
            && spaceComment is not null
            && spaceComment.Contains("space is tight", StringComparison.OrdinalIgnoreCase)
            && spaceComment.Contains("availability needs to be confirmed", StringComparison.OrdinalIgnoreCase)
        ));
        Assert.IsTrue(mappedRows.All(row =>
            row.Values.TryGetValue("Remarks", out var remarks)
            && remarks is not null
            && remarks.Contains("further increase", StringComparison.OrdinalIgnoreCase)
        ));

        Assert.IsTrue(mappedRows.Any(row =>
            row.Values.TryGetValue("Carrier", out var carrier)
            && string.Equals(carrier, "ONE", StringComparison.OrdinalIgnoreCase)
            && row.Values.TryGetValue("Remarks", out var remarks)
            && remarks is not null
            && remarks.Contains("overweight surcharge", StringComparison.OrdinalIgnoreCase)
        ));
        Assert.IsFalse(mappedRows.Any(row =>
            row.Values.TryGetValue("Carrier", out var carrier)
            && !string.Equals(carrier, "ONE", StringComparison.OrdinalIgnoreCase)
            && row.Values.TryGetValue("Remarks", out var remarks)
            && remarks is not null
            && remarks.Contains("overweight surcharge", StringComparison.OrdinalIgnoreCase)
        ));
        Assert.IsTrue(mappedRows.All(row =>
            row.Values.TryGetValue("Commodity", out var commodity)
            && string.Equals(commodity, "General Cargo", StringComparison.OrdinalIgnoreCase)
        ));
        Assert.IsTrue(mappedRows.Any(row =>
            row.Values.TryGetValue("Carrier", out var carrier)
            && string.Equals(carrier, "MSC", StringComparison.OrdinalIgnoreCase)
            && row.Values.TryGetValue("ValidFrom", out var validFrom)
            && validFrom == "15-Aug"
            && row.Values.TryGetValue("ValidTo", out var validTo)
            && validTo == "21-Aug"
        ));
    }

    [TestMethod]
    public async Task StackedIndicativeRatesEmail_ExtractsAllRowsWithPolAndPoe()
    {
        const string body = """
            Dear Marco,

            Pls check below update rates for your ref.

            POL
            POD
            CARRIER
            20'
            40'/40HC
            Free time
            Effective Date
            Expiry date

            Shanghai/ Ningbo
            Caldera
            PIL
            $6,560
            $6,830
            18 days
            8-Aug
            14-Aug

            Xingang/Qingdao
            Caldera
            PIL
            $7,100
            $7,400
            18 days
            8-Aug
            14-Aug

            Shanghai/ Ningbo/ Qingdao/Xingang/Shenzhen/Xiamen
            Caldera
            MSC
            $6,615
            $7,515
            14 days
            8-Aug
            14-Aug

            Shanghai/ Ningbo/ Qingdao/Xingang/Shenzhen/Xiamen
            Caldera
            ONE
            $7,015
            $7,315
            16 days
            8-Aug
            14-Aug

            Shanghai/ Ningbo/ Qingdao/Xingang/Shenzhen/Xiamen
            Caldera
            CMA
            $6,644
            $7,344
            21 days
            8-Aug
            14-Aug

            Shanghai/ Ningbo/ Qingdao/Xingang/Shenzhen/Xiamen
            Moin
            ONE
            $7,415
            $7,515
            12 days
            8-Aug
            14-Aug

            Shanghai/ Ningbo/ Qingdao/Xingang/Shenzhen/Xiamen
            Moin
            MSC
            $7,765
            $7,915
            14 days
            8-Aug
            14-Aug

            Shanghai/ Ningbo/ Qingdao/Xingang/Shenzhen/Xiamen
            Rodman
            MSC
            $5,415
            $6,215
            14 days
            8-Aug
            14-Aug

            Shanghai/ Ningbo/ Qingdao/Xingang/Shenzhen/Xiamen
            Cristobal/Colon
            MSC
            $6,765
            $7,115
            14 days
            8-Aug
            14-Aug

            Shanghai/ Ningbo/ Qingdao/Xingang/Shenzhen/Xiamen
            Manzanillo
            ONE
            $6,575
            $6,375
            12 days
            8-Aug
            14-Aug

            Shanghai/ Ningbo/ Qingdao/Xingang/Shenzhen/Xiamen
            Rodman
            ONE
            $6,515
            $7,140
            12 days
            8-Aug
            14-Aug

            Shanghai/Ningbo/Qingdao
            Colon/Manzanillo
            OOCL
            $6,635
            $6,755
            12 days
            8-Aug
            14-Aug

            Xingang/Xiamen
            Colon/Manzanillo
            OOCL
            $6,685
            $6,805
            12 days
            8-Aug
            14-Aug

            General Cargo
            Subject to DTHC and local charges at both ends
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
        Assert.HasCount(13, table.Rows);
        Assert.IsTrue(table.Rows.All(row =>
            !string.IsNullOrWhiteSpace(row.Values["POL"])
            && !string.IsNullOrWhiteSpace(row.Values["POE"])
            && !string.IsNullOrWhiteSpace(row.Values["Carrier"])
        ));

        var mappedRows = await new ColumnMappingService(null!).MapAsync(document);
        Assert.IsTrue(mappedRows.Count > table.Rows.Count);
        Assert.IsTrue(mappedRows.All(row =>
            row.SourceSheetName == "EMAIL FCL Cell Stream"
            && row.Values.TryGetValue("OriginPort", out var pol)
            && !string.IsNullOrWhiteSpace(pol)
            && row.Values.TryGetValue("PortOfExit", out var poe)
            && !string.IsNullOrWhiteSpace(poe)
            && row.Values.TryGetValue("ContainerType", out var equipment)
            && !string.IsNullOrWhiteSpace(equipment)
            && row.Values.TryGetValue("Carrier", out var carrier)
            && !string.IsNullOrWhiteSpace(carrier)
        ));
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
        rates.Cell("A11").Value = "Cargos locales sujetos al 13% de IVA.";
        rates.Cell("A12").Value = "Todo cliente nuevo deberá cancelar un depósito de garantía de $1000 por contenedor.";
        rates.Cell("A13").Value = "Peso máximo permitido sin cargo por sobre peso: 21,5 TON.";
        rates.Cell("A14").Value = "Costo por sobre peso $150, posterior a 21.5 tons.";

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
        Assert.Contains("13% de IVA", first.Values["Remarks"]!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("USD 1,000", first.Values["Remarks"]!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("21.5 toneladas", first.Values["Remarks"]!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("USD 150", first.Values["Remarks"]!, StringComparison.OrdinalIgnoreCase);
        Assert.IsTrue(first.Values["ValidFrom"]!.EndsWith("-08-08", StringComparison.Ordinal));
        Assert.IsTrue(first.Values["ValidTo"]!.EndsWith("-08-14", StringComparison.Ordinal));
        Assert.IsFalse(table.Headers.Contains("POL Additional TAO", StringComparer.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task Pdf_AgunsaPilMatrix_WithMergedRegionAndNoCarrierColumn_ExtractsAllRows()
    {
        var fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "agunsa_pil_august_2026.pdf"
        );

        var extractor = new PdfDocumentExtractor();
        var document = await extractor.ExtractAsync(
            new DocumentExtractionInput(
                "agunsa_pil_august_2026.pdf",
                "application/pdf",
                ".pdf",
                await File.ReadAllBytesAsync(fixturePath)
            )
        );

        var table = document.Tables.Single();
        Assert.AreEqual("PDF Carrier Tariff Matrix", table.SheetName);
        Assert.HasCount(73, table.Rows);
        Assert.AreEqual("Qingdao", table.Rows.First().Values["POL"]);
        Assert.AreEqual("Caldera", table.Rows.First().Values["POE"]);
        Assert.AreEqual("PIL", table.Rows.First().Values["Carrier"]);
        Assert.AreEqual("7 days", table.Rows.First().Values["Free Time"]);
        Assert.AreEqual("$8 108,00", table.Rows.First().Values["20'"]);
        Assert.AreEqual("15/08/2026 AL 21/08/2026", table.Rows.First().Values["Validity"]);
        Assert.IsTrue(table.Rows.Any(row => row.Values["POL"] == "Chittagong"));
        Assert.IsTrue(table.Rows.Any(row => row.Values["POL"] == "Colombo"));

        var mappedRows = await new ColumnMappingService(null!).MapAsync(document);
        Assert.HasCount(219, mappedRows);
        Assert.IsTrue(mappedRows.All(row => row.Values["OriginPort"] is not null));
        Assert.IsTrue(mappedRows.All(row => row.Values["PortOfExit"] == "Caldera"));
        Assert.IsTrue(mappedRows.All(row => row.Values["Carrier"] == "PIL"));
        Assert.IsTrue(mappedRows.Any(row => row.Values["ContainerType"] == "20DV"));
        Assert.IsTrue(mappedRows.Any(row => row.Values["ContainerType"] == "40DV"));
        Assert.IsTrue(mappedRows.Any(row => row.Values["ContainerType"] == "40HC"));

        var records = await new Dhole.DataExtraction.Infrastructure.Normalization.PricingRecordNormalizer()
            .NormalizeAsync(Guid.NewGuid(), Guid.NewGuid(), mappedRows);
        Assert.IsTrue(records.All(record => record.ValidFrom == new DateTime(2026, 8, 15)));
        Assert.IsTrue(records.All(record => record.ValidTo == new DateTime(2026, 8, 21)));
        Assert.IsTrue(records.All(record => record.FreeDays == 7));
        Assert.IsTrue(records.All(record => record.OceanFreight.HasValue));
        CollectionAssert.AreEqual(
            new[] { 8108m, 8316m, 8316m },
            records
                .Where(record => record.OriginPort == "Qingdao")
                .Select(record => record.OceanFreight!.Value)
                .OrderBy(value => value)
                .ToArray()
        );
        Assert.IsTrue(records.All(record =>
            !record.OceanFreight.HasValue
            || Math.Abs(record.OceanFreight.Value) <= 99_999_999_999_999.9999m
        ));
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
    [TestMethod]
    public async Task PlainTextEmail_WithCorporateSignatureBeforeForwardedWwlThread_ExtractsNewestRatesOnly()
    {
        const string body = """
            [cid:a890ddd2-246a-4f52-a9bd-13108fb8a556]
            Website : https://logisticacastrofallas.com
            Online Cargo Tracking->
            Tracking your shipments on https://logisticacastrofallas.com/#/web-tracking
            REDES SOCIALES:
            Facebook Grupo Castro Fallas / LinkedIn Grupo Castro Fallas / Instagram Grupo Castro Fallas
            AVISO LEGAL: Este mensaje es confidencial, puede contener información privilegiada.
            The information contained in this message is privileged and intended only for the recipients named.
            ---
            De: Veronica.jiang <veronica.jiang@wwl.sg>
            Enviado: miércoles, 12 de agosto de 2026 03:19
            Para: Royner Sibaja <rsibaja@castrofallas.com>; Marco Artavia <pricing@castrofallas.com>
            Cc: Andreu Zhou <Andreu.Zhou@wwl.sg>
            Asunto: UPDATE FAK WWL / CASTRO FALLAS / 12-AUG

            Dear Royner,
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
            15 Aug-21 Aug
            $7,400
            $8,300
            $8,300
            SHA/NGB/SZN/XMN/TAO/TSN/DLN
            Acajulta/Corinto/Puerto Caldera
            MSC Basket
            21 days dry
            15 Aug-21 Aug
            $7,300
            $8,100
            $8,100
            SHA/NGB/SZN/XMN/TAO
            Acajulta/Corinto/Puerto Caldera
            ONE FAK
            16 days dry
            15 Aug-21 Aug
            $7,500
            $7,800
            $7,800
            SHANGHAI
            Acajulta/Corinto/Puerto Caldera
            PIL
            18 days dry
            14 Aug-20 Aug
            $7,700
            $7,900
            $7,900
            Sub to p/s $50/cntr & MBL RLS $75/BILL
            ---
            Un saludo cordial
            Veronica Jiang
            Worldwide Logistics Co., Ltd.
            ·¢¼þÈË: Veronica.jiang <veronica.jiang@wwl.sg>
            ·¢ËÍÊ±¼ä: 2026Äê7ÔÂ31ÈÕ 19:21
            Ö÷Ìâ: UPDATE FAK WWL / CASTRO FALLAS /31-JULY
            Dear Royner.
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
            """;

        var extractor = new EmailDocumentExtractor();
        var document = await extractor.ExtractAsync(
            new DocumentExtractionInput(
                "wwl-thread.txt",
                "text/plain",
                ".txt",
                Encoding.UTF8.GetBytes(body)
            )
        );

        var table = document.Tables.Single();
        Assert.AreEqual("EMAIL FCL Cell Stream", table.SheetName);
        Assert.HasCount(4, table.Rows);
        var rows = table.Rows.ToArray();
        Assert.AreEqual("15 Aug", rows[0].Values["ValidFrom"]);
        Assert.AreEqual("21 Aug", rows[0].Values["ValidTo"]);
        Assert.AreEqual("$7,400", rows[0].Values["20GP"]);
        Assert.AreEqual("$8,300", rows[0].Values["40HQ"]);
        Assert.IsFalse(document.RawText.Contains("$6,600", StringComparison.Ordinal));
    }

}
