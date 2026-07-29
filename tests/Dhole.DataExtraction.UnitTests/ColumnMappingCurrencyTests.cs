using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using Dhole.DataExtraction.Infrastructure.Mapping;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class ColumnMappingCurrencyTests
{
    [TestMethod]
    public async Task MapAsync_WhenCurrencyHeaderIncludesContainerText_MapsCurrency()
    {
        var values = new Dictionary<string, string?>
        {
            ["POL"] = "Lianyungang",
            ["POE"] = "Moin",
            ["Naviera"] = "Maersk",
            ["Moneda 20'"] = "USD",
            ["40'/40HC"] = "$6,430",
            ["Inicio"] = "14",
            ["Vence"] = "01-Aug-2026 31-Aug-2026",
        };
        var document = new ExtractedDocument(
            "rates.pdf",
            SourceFileType.Pdf,
            [new ExtractedTable("PDF Visual Table 1", values.Keys.ToArray(), [new ExtractedRow(7, values)])]
        );

        var rows = await new ColumnMappingService(null!).MapAsync(document);

        Assert.IsTrue(rows.Count >= 1);
        Assert.IsTrue(rows.All(row => row.Values["Currency"] == "USD"));
    }

    [TestMethod]
    public async Task MapAsync_WhenAmountUsesDollarSymbol_InfersUsd()
    {
        var values = new Dictionary<string, string?>
        {
            ["POL"] = "Shanghai",
            ["POE"] = "Caldera",
            ["Naviera"] = "CMA CGM",
            ["40DV"] = "$6,280",
            ["Inicio"] = "01-Aug-2026",
            ["Vence"] = "31-Aug-2026",
        };
        var document = new ExtractedDocument(
            "rates.pdf",
            SourceFileType.Pdf,
            [new ExtractedTable("PDF", values.Keys.ToArray(), [new ExtractedRow(5, values)])]
        );

        var rows = await new ColumnMappingService(null!).MapAsync(document);

        Assert.AreEqual("USD", rows.Single().Values["Currency"]);
    }
}
