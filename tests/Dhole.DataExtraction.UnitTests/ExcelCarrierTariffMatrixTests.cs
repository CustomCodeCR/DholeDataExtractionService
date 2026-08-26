using ClosedXML.Excel;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Infrastructure.Extraction.Excel;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class ExcelCarrierTariffMatrixTests
{
    [TestMethod]
    public async Task ExtractAsync_MscDtMoinMatrix_PreservesBaseTariffRows()
    {
        byte[] bytes;
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("DT Moin");
            sheet.Cell(1, 1).Value = "DT MOIN del 01 al 06 de SEPTIEMBRE 2026";

            sheet.Cell(6, 1).Value = "POL";
            sheet.Cell(6, 2).Value = "20 DV";
            sheet.Cell(6, 3).Value = "40DV/HC";
            sheet.Cell(6, 6).Value = "POL Additional TAO";
            sheet.Cell(6, 7).Value = "Country";
            sheet.Cell(6, 10).Value = "20DV";
            sheet.Cell(6, 11).Value = "40DV/HC";

            sheet.Cell(7, 1).Value = "Shekou";
            sheet.Cell(7, 2).Value = 11250;
            sheet.Cell(7, 3).Value = 11200;
            sheet.Cell(7, 6).Value = "Shekou";
            sheet.Cell(7, 7).Value = "China";
            sheet.Cell(7, 10).Value = 500;
            sheet.Cell(7, 11).Value = 700;

            sheet.Cell(8, 1).Value = "Hong Kong";
            sheet.Cell(8, 2).Value = 11250;
            sheet.Cell(8, 3).Value = 11200;

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            bytes = stream.ToArray();
        }

        var extractor = new ExcelDocumentExtractor();
        var document = await extractor.ExtractAsync(
            new DocumentExtractionInput(
                "MSC DT MOIN - Validez 01 al 06 de SEPTIEMBRE.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".xlsx",
                bytes
            )
        );

        Assert.AreEqual(1, document.Tables.Count);
        var table = document.Tables.Single();
        Assert.AreEqual(2, table.Rows.Count);
        Assert.IsTrue(table.SheetName?.Contains("FCL normalizado", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(table.Headers.Contains("POL Additional TAO", StringComparer.OrdinalIgnoreCase));

        var first = table.Rows.OrderBy(row => row.RowNumber).First();
        Assert.AreEqual("Shekou", first.Values["POL"]);
        Assert.AreEqual("Moín", first.Values["POE"]);
        Assert.AreEqual("MSC", first.Values["Carrier"]);
        Assert.AreEqual("USD", first.Values["Currency"]);
        Assert.AreEqual("2026-09-01", first.Values["ValidFrom"]);
        Assert.AreEqual("2026-09-06", first.Values["ValidTo"]);
        Assert.AreEqual("11250", first.Values["20 DV"]);
        Assert.AreEqual("11200", first.Values["40DV/HC"]);
    }
}
