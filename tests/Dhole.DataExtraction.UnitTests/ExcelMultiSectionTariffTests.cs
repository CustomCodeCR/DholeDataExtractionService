using ClosedXML.Excel;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Infrastructure.Extraction.Excel;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class ExcelMultiSectionTariffTests
{
    [TestMethod]
    public async Task ExtractAsync_RepeatedHeadersAndMergedRoutes_AreSeparateUsableTables()
    {
        byte[] bytes;
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Competencia");
            sheet.Cell(1, 1).Value = "Tarifario octubre";
            sheet.Cell(2, 1).Value = "POL";
            sheet.Cell(2, 2).Value = "POD";
            sheet.Cell(2, 3).Value = "Ocean Freight";
            sheet.Cell(2, 4).Value = "Carrier";
            sheet.Cell(3, 1).Value = "Shanghai";
            sheet.Range("A3:A4").Merge();
            sheet.Cell(3, 2).Value = "Moin";
            sheet.Cell(3, 3).Value = 1200;
            sheet.Cell(3, 4).Value = "PIL";
            sheet.Cell(4, 2).Value = "Caldera";
            sheet.Cell(4, 3).Value = 1400;
            sheet.Cell(4, 4).Value = "PIL";
            sheet.Cell(6, 1).Value = "POL";
            sheet.Cell(6, 2).Value = "POD";
            sheet.Cell(6, 3).Value = "Ocean Freight";
            sheet.Cell(6, 4).Value = "Carrier";
            sheet.Cell(7, 1).Value = "Ningbo";
            sheet.Cell(7, 2).Value = "Moin";
            sheet.Cell(7, 3).Value = 1750;
            sheet.Cell(7, 4).Value = "MSC";

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            bytes = stream.ToArray();
        }

        var extractor = new ExcelDocumentExtractor();
        var document = await extractor.ExtractAsync(
            new DocumentExtractionInput(
                "competencia-secciones.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".xlsx",
                bytes
            )
        );

        Assert.AreEqual(2, document.Tables.Count);
        var tables = document.Tables.ToArray();
        Assert.AreEqual(2, tables[0].Rows.Count);
        Assert.AreEqual("Shanghai", tables[0].Rows.ElementAt(1).Values["POL"]);
        Assert.AreEqual("Caldera", tables[0].Rows.ElementAt(1).Values["POD"]);
        Assert.AreEqual("Ningbo", tables[1].Rows.Single().Values["POL"]);
        Assert.AreEqual("1750", tables[1].Rows.Single().Values["Ocean Freight"]);
    }

    [TestMethod]
    public async Task ExtractAsync_LateHeader_IsFoundAfterLongIntroduction()
    {
        byte[] bytes;
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("Tarifas");
            for (var index = 1; index <= 50; index++)
                sheet.Cell(index, 1).Value = $"Condición general {index}";
            sheet.Cell(65, 1).Value = "POL";
            sheet.Cell(65, 2).Value = "POD";
            sheet.Cell(65, 3).Value = "Ocean Freight";
            sheet.Cell(66, 1).Value = "Qingdao";
            sheet.Cell(66, 2).Value = "Moin";
            sheet.Cell(66, 3).Value = 998;

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            bytes = stream.ToArray();
        }

        var extractor = new ExcelDocumentExtractor();
        var document = await extractor.ExtractAsync(
            new DocumentExtractionInput(
                "competencia-cabecera-tardia.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".xlsx",
                bytes
            )
        );
        Assert.AreEqual("Qingdao", document.Tables.Single().Rows.Single().Values["POL"]);
    }
}
