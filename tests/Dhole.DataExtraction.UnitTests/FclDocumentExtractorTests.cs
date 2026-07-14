using System.Text;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Infrastructure.Extraction.Email;
using Dhole.DataExtraction.Infrastructure.Extraction.Pdf;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class FclDocumentExtractorTests
{
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
        Assert.AreEqual(4, table.Rows.Count);
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
        Assert.AreEqual(4, table.Rows.Count);
        CollectionAssert.Contains(table.Headers.ToArray(), "Free Time");
        CollectionAssert.Contains(table.Headers.ToArray(), "Effective");
        CollectionAssert.Contains(table.Headers.ToArray(), "Expiry");
        Assert.AreEqual("18 días", table.Rows.First().Values["Free Time"]);
        Assert.AreEqual("15-Jul-2026", table.Rows.First().Values["Effective"]);
        Assert.AreEqual("31-Jul-2026", table.Rows.First().Values["Expiry"]);
    }
}
