using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using Dhole.DataExtraction.Infrastructure.Mapping;
using Dhole.DataExtraction.Infrastructure.Normalization;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class ApprovedPricingTemplateMappingTests
{
    [TestMethod]
    public async Task MapAsync_ApprovedTemplateHeaders_MapsRequiredFieldsWithoutAi()
    {
        var values = new Dictionary<string, string?>
        {
            ["Carrier"] = "MSK",
            ["Equipo"] = "40HC",
            ["Cantidad"] = "1",
            ["POL"] = "SHANGHAI",
            ["POE"] = "BALBOA",
            ["POD"] = null,
            ["Flete Internacional"] = "6600",
            ["Moneda"] = "USD",
            ["Tipo Tarifa"] = "SPOT",
            ["ETD"] = "26/09/2026",
            ["Válido Desde"] = "26/09/2026",
            ["Válido Hasta"] = "26/09/2026",
            ["Tiempo Tránsito (días)"] = "36",
            ["Días Libres en Destino"] = "7",
            ["Observaciones"] = null,
        };
        var document = new ExtractedDocument(
            "Plantilla_Extraccion REVISADO 10-9-26.xlsx",
            SourceFileType.Excel,
            [
                new ExtractedTable(
                    "Tarifas",
                    values.Keys.ToArray(),
                    [new ExtractedRow(2, values)]
                )
            ]
        );

        var rows = await new ColumnMappingService(null!).MapAsync(document);

        var row = rows.Single();
        Assert.AreEqual("SHANGHAI", row.Values["OriginPort"]);
        Assert.AreEqual("BALBOA", row.Values["PortOfExit"]);
        Assert.AreEqual("40HC", row.Values["ContainerType"]);
        Assert.AreEqual("MSK", row.Values["Carrier"]);
        Assert.AreEqual("USD", row.Values["Currency"]);
        Assert.AreEqual("6600", row.Values["OceanFreight"]);
        Assert.AreEqual("26/09/2026", row.Values["ValidFrom"]);
        Assert.AreEqual("26/09/2026", row.Values["ValidTo"]);
        Assert.AreEqual("36", row.Values["TransitDays"]);
        Assert.AreEqual("7", row.Values["FreeDays"]);
    }

    [TestMethod]
    public void Normalize_ApprovedTemplateHeaders_UsesCanonicalDeterministicKeys()
    {
        Assert.AreEqual("validfrom", ColumnHeaderNormalizer.Normalize("Válido Desde"));
        Assert.AreEqual("validto", ColumnHeaderNormalizer.Normalize("Válido Hasta"));
        Assert.AreEqual(
            "oceanfreight",
            ColumnHeaderNormalizer.Normalize("Flete Internacional")
        );
        Assert.AreEqual(
            "transitdays",
            ColumnHeaderNormalizer.Normalize("Tiempo Tránsito (días)")
        );
        Assert.AreEqual(
            "freedays",
            ColumnHeaderNormalizer.Normalize("Días Libres en Destino")
        );
    }

    [TestMethod]
    public void NormalizeCarrier_Msk_ResolvesToMaersk()
    {
        Assert.AreEqual("MAERSK", CarrierNameNormalizer.Normalize("MSK"));
    }
}
