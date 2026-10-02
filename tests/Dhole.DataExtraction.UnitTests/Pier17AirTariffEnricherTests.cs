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
    public void Enrich_Pier17MiamiTariff_RecognizesAirAndPreservesPublishedBreakpoints()
    {
        const string rawText = """
            TARIFARIO AIR DIVISION Fecha: 1/10/2026
            Validez: 30/10/2026
            MIAMI
            Aeropuerto Mínimo +100 500 +1000 Aerolinea Ruta Salidas Cut-off (Origen) Tránsito Servicio
            MIA $ 55,00 $ 1,20 $ 1,20 $ 1,35 Avianca Directo Miercoles 24 horas antes 1 días Consolidado
            MIA $ 160,00 $ 1,20 $ 1,20 $ 1,35 Avianca Directo Diarias 24 horas antes 1 días B2B
            Tarifa aplica por KgVol Currency: USD
            FCA Warehouse MIA
            NOTAS DE PIER17 COSTA RICA
            El destino de nuestro consolidado aéreo es Corporación Ebba
            """;

        var document = new ExtractedDocument(
            "Tarifarios Pier 17 MIA Octubre.pdf",
            SourceFileType.Pdf,
            [new ExtractedTable("PDF", [], [])],
            rawText
        );

        var enriched = InvokeEnricher(
            document,
            string.Empty,
            string.Empty
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
        Assert.IsTrue(rows.All(row => row.Values["Carrier"] == "Avianca"));
        Assert.IsTrue(rows.All(row => row.Values["Currency"] == "USD"));
        Assert.IsTrue(rows.All(row => row.Values["ValidFrom"] == "2026-10-01"));
        Assert.IsTrue(rows.All(row => row.Values["ValidTo"] == "2026-10-30"));
        Assert.AreEqual("55", rows[0].Values["MinimumRate"]);
        Assert.AreEqual("1.2", rows[0].Values["OceanFreight"]);
        Assert.AreEqual("1.2", rows[0].Values["AirRatePlus100"]);
        Assert.AreEqual("1.2", rows[0].Values["AirRatePlus500"]);
        Assert.AreEqual("1.35", rows[0].Values["AirRatePlus1000"]);
        Assert.AreEqual("AIR_CONSOLIDATED", rows[0].Values["ServiceMode"]);
        Assert.AreEqual("AIR_BACK_TO_BACK", rows[1].Values["ServiceMode"]);
        StringAssert.Contains(rows[0].Values["Remarks"], "+100 1.2 USD");
        StringAssert.Contains(rows[0].Values["Remarks"], "+500 1.2 USD");
        StringAssert.Contains(rows[0].Values["Remarks"], "+1000 1.35 USD");
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
