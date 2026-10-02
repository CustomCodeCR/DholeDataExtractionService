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

    [TestMethod]
    public void Enrich_Pier17MiamiSubject_DefaultsMiaToSjoAndMonthlyValidity()
    {
        const string rawText = """
            TARIFARIO AIR DIVISION
            Aeropuerto Mínimo Flete +100 Flete +300 Flete +500 Aerolinea Ruta Salidas Cut-off Tránsito Servicio
            MIA $ 75.00 $ 2.65 $ 2.45 $ 2.30 American Airlines Directo Martes Viernes 1 Días Consolidado
            $ 175.00 $ 2.95 $ 2.75 $ 2.60 American Airlines Directo Martes Viernes 1 Días B2B
            Tarifa aplica por KgVol
            """;

        var document = new ExtractedDocument(
            "PIER17 MIAMI.pdf",
            SourceFileType.Pdf,
            [new ExtractedTable("PDF", [], [])],
            rawText
        );

        var enriched = InvokeEnricher(
            document,
            "Re: TARIFARIO AEREO PIER 17 MIAMI / OCTUBRE 2026",
            "Favor notar que se adjunta el tarifario de Miami para nuestro servicio AEREO."
        );

        var rows = enriched.Tables
            .SelectMany(table => table.Rows)
            .Where(row =>
                row.Values.TryGetValue("ContainerType", out var equipment)
                && string.Equals(equipment, "AIR", StringComparison.OrdinalIgnoreCase)
            )
            .ToArray();

        Assert.AreEqual(2, rows.Length);
        Assert.IsTrue(rows.All(row => row.Values["OriginPort"] == "MIA"));
        Assert.IsTrue(rows.All(row => row.Values["PortOfExit"] == "SJO"));
        Assert.IsTrue(rows.All(row => row.Values["Currency"] == "USD"));
        Assert.IsTrue(rows.All(row => row.Values["ValidFrom"] == "2026-10-01"));
        Assert.IsTrue(rows.All(row => row.Values["ValidTo"] == "2026-10-31"));
        Assert.AreEqual("2.65", rows[0].Values["OceanFreight"]);
        Assert.AreEqual("2.45", rows[0].Values["AirRatePlus300"]);
        Assert.AreEqual("2.3", rows[0].Values["AirRatePlus500"]);
        Assert.AreEqual("AIR_CONSOLIDATED", rows[0].Values["ServiceMode"]);
        Assert.AreEqual("AIR_BACK_TO_BACK", rows[1].Values["ServiceMode"]);
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
