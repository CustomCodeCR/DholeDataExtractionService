using System.Text;
using ClosedXML.Excel;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Infrastructure.Extraction.Csv;
using Dhole.DataExtraction.Infrastructure.Extraction.Email;
using Dhole.DataExtraction.Infrastructure.GrpcClients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class HtmlStructuredExtractionTests
{
    private const string FreightTable = """
        <html><body><table>
          <tr><th>POL</th><th>POD</th><th>CARRIER</th><th>20'</th><th>40'/40HC</th><th>Free time</th><th>Effective Date</th><th>Expiry date</th></tr>
          <tr><td>Shanghai/Ningbo/Qingdao</td><td rowspan="3">Caldera</td><td>OOCL</td><td>$6,740</td><td>$7,065</td><td>18 days</td><td>1-Nov</td><td>7-Nov</td></tr>
          <tr><td>Xingang/Xiamen</td><td>OOCL</td><td>$6,790</td><td>$7,115</td><td>18 days</td><td>1-Nov</td><td>7-Nov</td></tr>
          <tr><td>China Base Ports</td><td>MSC</td><td>$6,915</td><td>$7,315</td><td>14 days</td><td>28-Oct</td><td>8-Nov</td></tr>
        </table></body></html>
        """;

    [TestMethod]
    public void HtmlTable_PropagatesMergedPodWithoutDuplicatingAmounts()
    {
        var table = HtmlTariffTableReader.Extract(FreightTable).Single();
        Assert.AreEqual(3, table.Rows.Count);

        var rows = table.Rows.ToArray();
        Assert.AreEqual("Caldera", rows[0].Values["POD"]);
        Assert.AreEqual("Caldera", rows[1].Values["POD"]);
        Assert.AreEqual("Caldera", rows[2].Values["POD"]);
        Assert.AreEqual("$6,790", rows[1].Values["20'"]);
        Assert.AreEqual("$7,115", rows[1].Values["40'/40HC"]);
        Assert.AreEqual("28-Oct", rows[2].Values["Effective Date"]);
        Assert.AreEqual("8-Nov", rows[2].Values["Expiry date"]);
    }

    [TestMethod]
    public async Task HtmlDocument_UsesStructuredTableBeforeFlattenedEmailText()
    {
        var input = new DocumentExtractionInput(
            "rate.html", "text/html", ".html", Encoding.UTF8.GetBytes(FreightTable)
        );
        var result = await new EmailDocumentExtractor().ExtractAsync(input);
        Assert.AreEqual(3, result.Tables.Single().Rows.Count);
        Assert.AreEqual("Caldera", result.Tables.Single().Rows.Last().Values["POD"]);
    }

    [TestMethod]
    public void CanonicalHtml_EscapesUntrustedCellContent()
    {
        var source = FreightTable.Replace("Caldera", "Caldera &amp; &lt;script&gt;alert(1)&lt;/script&gt;");
        var tables = HtmlTariffTableReader.Extract(source);
        var output = HtmlTariffTableReader.ToCanonicalHtml(tables);
        Assert.IsTrue(output.Contains("&lt;script&gt;alert(1)&lt;/script&gt;"));
        Assert.IsFalse(output.Contains("<script>"));
    }

    [TestMethod]
    public void HtmlTable_ColspanDoesNotInventSecondPrice()
    {
        const string html = """
            <table><tr><th>POL</th><th>POD</th><th>20'</th><th>40'</th></tr>
            <tr><td>Ningbo</td><td>Caldera</td><td colspan="2">$1,000</td></tr></table>
            """;
        var row = HtmlTariffTableReader.Extract(html).Single().Rows.Single();
        Assert.AreEqual("$1,000", row.Values["20'"]);
        Assert.IsNull(row.Values["40'"]);
    }

    [TestMethod]
    public async Task Csv_BlankHeaderKeepsOriginalColumnPositions()
    {
        var content = Encoding.UTF8.GetBytes("POL,,POD,20'\nNingbo,ignored,Caldera,1000\n");
        var input = new DocumentExtractionInput("rates.csv", "text/csv", ".csv", content);
        var row = (await new CsvDocumentExtractor().ExtractAsync(input)).Tables.Single().Rows.Single();
        Assert.AreEqual("Caldera", row.Values["POD"]);
        Assert.AreEqual("1000", row.Values["20'"]);
    }

    [TestMethod]
    public async Task ExcelAiReader_PreservesVerticallyMergedDestination()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Rates");
        worksheet.Cell("A1").Value = "POL";
        worksheet.Cell("B1").Value = "POD";
        worksheet.Cell("C1").Value = "20'";
        worksheet.Cell("A2").Value = "Ningbo";
        worksheet.Cell("A3").Value = "Shanghai";
        worksheet.Cell("B2").Value = "Caldera";
        worksheet.Range("B2:B3").Merge();
        worksheet.Cell("C2").Value = 1000;
        worksheet.Cell("C3").Value = 1100;
        using var memory = new MemoryStream();
        workbook.SaveAs(memory);

        var reader = new AiEmailContentReader(
            new ConfigurationBuilder().Build(),
            NullLogger<AiEmailContentReader>.Instance
        );
        var evidence = await reader.ReadAsTextAsync("rates.xlsx", null, ".xlsx", memory.ToArray());
        Assert.IsTrue(evidence.Contains("Shanghai\tCaldera\t1100"));
    }
}
