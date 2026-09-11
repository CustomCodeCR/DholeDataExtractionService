using System.Globalization;
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
            ["Commodity"] = "Electrónicos",
            ["Válido Desde"] = "26/09/2026",
            ["Válido Hasta"] = "30/09/2026",
            ["Tiempo Tránsito (días)"] = "36",
            ["Días Libres en Destino"] = "7",
            ["Observaciones"] = "Sujeto a espacio",
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
        var expectedSpotDate = GetCostaRicaToday().ToString(
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture
        );

        Assert.AreEqual("SHANGHAI", row.Values["OriginPort"]);
        Assert.AreEqual("BALBOA", row.Values["PortOfExit"]);
        Assert.AreEqual("40HC", row.Values["ContainerType"]);
        Assert.AreEqual("MSK", row.Values["Carrier"]);
        Assert.AreEqual("USD", row.Values["Currency"]);
        Assert.AreEqual("6600", row.Values["OceanFreight"]);
        Assert.AreEqual(expectedSpotDate, row.Values["ValidFrom"]);
        Assert.AreEqual(expectedSpotDate, row.Values["ValidTo"]);
        Assert.AreEqual("36", row.Values["TransitDays"]);
        Assert.AreEqual("7", row.Values["FreeDays"]);
        Assert.AreEqual("Electrónicos", row.Values["Commodity"]);
        StringAssert.Contains(row.Values["Remarks"], "Sujeto a espacio");
        StringAssert.Contains(row.Values["Remarks"], "ETD: 26/09/2026");
        StringAssert.Contains(row.Values["Remarks"], "Commodity: Electrónicos");
        Assert.AreEqual(row.Values["Remarks"], row.Values["SpaceComment"]);
    }

    [TestMethod]
    public async Task MapAsync_NonSpotRate_PreservesProvidedValidity()
    {
        var values = new Dictionary<string, string?>
        {
            ["Carrier"] = "MSK",
            ["Equipo"] = "40HC",
            ["POL"] = "SHANGHAI",
            ["POE"] = "BALBOA",
            ["Flete Internacional"] = "6600",
            ["Moneda"] = "USD",
            ["Tipo Tarifa"] = "FAK",
            ["ETD"] = "26/09/2026",
            ["Válido Desde"] = "20/09/2026",
            ["Válido Hasta"] = "30/09/2026",
        };
        var document = new ExtractedDocument(
            "non-spot.xlsx",
            SourceFileType.Excel,
            [
                new ExtractedTable(
                    "Tarifas",
                    values.Keys.ToArray(),
                    [new ExtractedRow(2, values)]
                )
            ]
        );

        var row = (await new ColumnMappingService(null!).MapAsync(document)).Single();

        Assert.AreEqual("20/09/2026", row.Values["ValidFrom"]);
        Assert.AreEqual("30/09/2026", row.Values["ValidTo"]);
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

    private static DateTime GetCostaRicaToday()
    {
        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Costa_Rica");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone).Date;
        }
        catch (TimeZoneNotFoundException)
        {
            return DateTime.UtcNow.Date;
        }
        catch (InvalidTimeZoneException)
        {
            return DateTime.UtcNow.Date;
        }
    }
}
