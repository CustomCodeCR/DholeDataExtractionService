using System.Text;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Infrastructure.Extraction.Email;
using Dhole.DataExtraction.Infrastructure.Extraction.Pdf;
using Dhole.DataExtraction.Infrastructure.Mapping;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class FclDocumentExtractorTests
{
    [TestMethod]
    public void RouteHeaders_MapEveryImportedDestinationToPoe()
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
            "PortOfExit",
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
            "PortOfExit",
            PricingRouteFieldSemantics.ResolveTargetField(
                "finaldestination",
                "DestinationPort"
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
}
