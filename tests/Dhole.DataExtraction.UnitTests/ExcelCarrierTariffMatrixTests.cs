using ClosedXML.Excel;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Infrastructure.Extraction.Excel;
using Dhole.DataExtraction.Infrastructure.Pipeline;

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

    [TestMethod]
    public async Task ExtractAsync_Pier17LclWorkbook_RecoversUnitRateAndFiltersNotes()
    {
        byte[] bytes;
        using (var workbook = new XLWorkbook())
        {
            var sheet = workbook.AddWorksheet("ASIA DIRECTO");
            sheet.Cell(1, 1).Value = "ASIA - COSTA RICA SERVICIO DIRECTO IMPORTACIÓN";
            sheet.Cell(3, 1).Value = "VÁLIDO DEL 1 AL 14 DE OCTUBRE DEL 2026";

            sheet.Cell(7, 1).Value = "Country";
            sheet.Cell(7, 2).Value = "Origin";
            sheet.Cell(7, 3).Value = "HUB";
            sheet.Cell(7, 4).Value = "Costo a Puerto";
            sheet.Cell(7, 5).Value = "EFS W/M";
            sheet.Cell(7, 6).Value = "Movimiento Interno + iva";
            sheet.Cell(7, 7).Value = "FLETE TOTAL SIN IVA (CBM/TO)";
            sheet.Cell(7, 8).Value = "Mínimo";
            sheet.Cell(7, 9).Value = "Frequency";
            sheet.Cell(7, 10).Value = "Transit Time";
            sheet.Cell(7, 11).Value = "Agentes";

            sheet.Cell(8, 1).Value = "China";
            sheet.Cell(8, 2).Value = "Shanghai";
            sheet.Cell(8, 3).Value = "Directo";
            sheet.Cell(8, 4).Value = 100;
            sheet.Cell(8, 5).Value = 8;
            sheet.Cell(8, 6).Value = 12;
            sheet.Cell(8, 7).Value = 120;
            sheet.Cell(8, 8).Value = 150;
            sheet.Cell(8, 9).Value = "Quincenal";
            sheet.Cell(8, 10).Value = "28 días";
            sheet.Cell(8, 11).Value = "Pier17 Shanghai";

            sheet.Cell(10, 1).Value =
                "CARGOS ADICIONALES Y NOTAS: estos textos no son filas tarifarias.";

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            bytes = stream.ToArray();
        }

        var extractor = new ExcelDocumentExtractor();
        var document = await extractor.ExtractAsync(
            new DocumentExtractionInput(
                "1era Quincena de Octubre PIER17 COSTA RICA - vip.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ".xlsx",
                bytes
            )
        );

        var enriched = PricingDocumentDefaultsEnricher.Enrich(document);
        var row = enriched.Tables.Single().Rows.Single();

        Assert.AreEqual("Shanghai", row.Values["OriginPort"]);
        Assert.AreEqual("LCL", row.Values["ContainerType"]);
        Assert.AreEqual("USD", row.Values["Currency"]);
        Assert.AreEqual("120", row.Values["OceanFreight"]);
        Assert.AreEqual("2026-10-01", row.Values["ValidFrom"]);
        Assert.AreEqual("2026-10-14", row.Values["ValidTo"]);
        Assert.IsFalse(row.Values.ContainsKey("Carrier"));
    }

}
