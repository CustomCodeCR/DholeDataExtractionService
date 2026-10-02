using System.Reflection;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using Dhole.DataExtraction.Infrastructure.Extraction.Pdf;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class Pier17AirTariffEnricherTests
{
    [TestMethod]
    public void Enrich_UsesEmailRouteAndParsesAirBreakpointRows()
    {
        const string rawText = """
            TARIFARIO AIR DIVISION Fecha: 1/10/2026
            Validez: 31/10/2026
            ESPAÑA
            Aeropuerto Mínimo Flete +100 Flete +300 Flete +500 Aerolinea Ruta Salidas Cut-off Tránsito Servicio
            MAD € 90,00 € 3,20 € 3,00 € 2,95 Iberojet Directo Sabados Jueves 1Días Consolidado
            € 255,00 € 3,20 € 3,00 € 2,95 Iberojet Directo Sabados Jueves 1Días B2B
            Tarifa aplica por KgVol Currency: EUR
            FCA Warehouse MAD
            """;

        const string emailBody =
            "Adjunto el tarifario de España (MAD) hacia SJO de manera consolidada y back to back.";

        var document = new ExtractedDocument(
            "Tarifarios Pier 17 MAD OCTUBRE.pdf",
            SourceFileType.Pdf,
            [new ExtractedTable("PDF", [], [])],
            rawText
        );

        var enriched = InvokeEnricher(document, "Tarifario PIER 17 ESPAÑA OCTUBRE", emailBody);

        var rows = enriched.Tables
            .SelectMany(table => table.Rows)
            .Where(row =>
                row.Values.TryGetValue("ContainerType", out var equipment)
                && string.Equals(equipment, "AIR", StringComparison.OrdinalIgnoreCase)
            )
            .ToArray();

        Assert.AreEqual(2, rows.Length);

        AssertAirRow(
            rows[0],
            minimum: "90",
            rate100: "3.2",
            serviceMode: "AIR_CONSOLIDATED"
        );
        AssertAirRow(
            rows[1],
            minimum: "255",
            rate100: "3.2",
            serviceMode: "AIR_BACK_TO_BACK"
        );

        Assert.AreEqual("3", rows[0].Values["AirRatePlus300"]);
        Assert.AreEqual("2.95", rows[0].Values["AirRatePlus500"]);
    }

    private static ExtractedDocument InvokeEnricher(
        ExtractedDocument document,
        string subject,
        string body
    )
    {
        var type = typeof(PdfDocumentExtractor).Assembly.GetType(
            "Dhole.DataExtraction.Infrastructure.Pipeline.PricingDocumentDefaultsEnricher",
            throwOnError: true
        )!;
        var method = type.GetMethod(
            "Enrich",
            BindingFlags.Public | BindingFlags.Static
        )!;

        return (ExtractedDocument)method.Invoke(
            null,
            [document, subject, body, null]
        )!;
    }

    private static void AssertAirRow(
        ExtractedRow row,
        string minimum,
        string rate100,
        string serviceMode
    )
    {
        Assert.AreEqual("MAD", row.Values["OriginPort"]);
        Assert.AreEqual("SJO", row.Values["PortOfExit"]);
        Assert.AreEqual("Iberojet", row.Values["Carrier"]);
        Assert.AreEqual("EUR", row.Values["Currency"]);
        Assert.AreEqual("2026-10-01", row.Values["ValidFrom"]);
        Assert.AreEqual("2026-10-31", row.Values["ValidTo"]);
        Assert.AreEqual(minimum, row.Values["MinimumRate"]);
        Assert.AreEqual(rate100, row.Values["OceanFreight"]);
        Assert.AreEqual("KG/VOL", row.Values["RateBasis"]);
        Assert.AreEqual(serviceMode, row.Values["ServiceMode"]);
        Assert.AreEqual("Directo", row.Values["AirlineRoute"]);
        Assert.AreEqual("1", row.Values["TransitDays"]);
    }
}
