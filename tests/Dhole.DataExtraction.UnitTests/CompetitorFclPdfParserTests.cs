using System.Globalization;
using System.Text;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Infrastructure.Extraction.Pdf;
using Dhole.DataExtraction.Infrastructure.Pipeline;
using UglyToad.PdfPig;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class CompetitorFclPdfParserTests
{
    [TestMethod]
    public async Task MgfCaldera_ExpandsOnlyPublishedPolsAndShared40Equipment()
    {
        var doc = await Extract("TARIFARIO MGF VALIDO DEL 16 AL 21 DE OCTUBRE_ CALDERA.pdf",
            Mgf("CAL", "16/10/2026", "6,480.00", "7,395.00"));
        var rows = doc.Tables.Single().Rows.ToArray();
        Assert.HasCount(15, rows);
        Assert.IsTrue(rows.All(row => row.Values["PortOfExit"] == "Caldera"));
        Assert.IsTrue(rows.All(row => row.Values["ValidFrom"] == "2026-10-16"));
        Assert.IsTrue(rows.All(row => row.Values["ValidTo"] == "2026-10-21"));
        Assert.AreEqual("6480", rows[0].Values["OceanFreight"]);
        Assert.AreEqual("7395", rows[1].Values["OceanFreight"]);
        Assert.AreEqual("7395", rows[2].Values["OceanFreight"]);
        CollectionAssert.AreEquivalent(new[] { "20DV", "40DV", "40HC" },
            rows.Take(3).Select(row => row.Values["ContainerType"]).ToArray());
        Assert.AreEqual("490", rows[0].Values["DestinationCharges"]); // 355 + 55 + 35 + 45
        Assert.AreEqual("915", rows[0].Values["Surcharges"]);
    }

    [TestMethod]
    public async Task MgfMoin_LeavesSuspiciousPublished40RateUnchangedAndFlagsIt()
    {
        var doc = await Extract("TARIFARIO MGF VALIDO DEL 15 AL 21 DE OCTUBRE_ MOIN.pdf",
            Mgf("MOIN", "15/10/2026", "9,865.00", "1,245.00", "EMC"));
        var rows = doc.Tables.Single().Rows.ToArray();
        Assert.HasCount(12, rows); // four published origins, three equipment types
        Assert.IsTrue(rows.All(row => row.Values["PortOfExit"] == "Moin"));
        Assert.AreEqual("2026-10-15", rows[0].Values["ValidFrom"]);
        Assert.AreEqual("1245", rows[1].Values["OceanFreight"]);
        Assert.AreEqual("true", rows[1].Values["RequiresManualReview"]);
    }

    [TestMethod]
    public async Task AlonsoCaldera_ExpandsFivePolsAndSeparatesPortChargesFromInland()
    {
        var doc = await Extract("PUERTOS BASE_FCL-CHINA-CALDERA.pdf", Alonso("CALDERA"));
        var rows = doc.Tables.Single().Rows.ToArray();
        Assert.HasCount(15, rows);
        Assert.AreEqual("6425", rows[0].Values["OceanFreight"]);
        Assert.AreEqual("820", rows[0].Values["DestinationCharges"]);
        Assert.AreEqual("720", rows[0].Values["Surcharges"]);
        Assert.AreEqual("7965", rows[0].Values["TotalSale"]);
        Assert.AreEqual("COSCO", rows[0].Values["Carrier"]);
        Assert.IsNull(rows[0].Values["ValidFrom"]);
        Assert.AreEqual("2026-10-21", rows[0].Values["ValidTo"]);
        var enriched = PricingDocumentDefaultsEnricher.Enrich(doc);
        Assert.IsNull(enriched.Tables.Single().Rows.First().Values["ValidFrom"]);
    }

    [TestMethod]
    public async Task AlonsoMoin_ExtractsBothEquipmentPricesAndExplicitDestination()
    {
        var rows = (await Extract("PUERTOS BASE_FCL-CHINA-MOIN.pdf", Alonso("MOIN")))
            .Tables.Single().Rows.ToArray();
        Assert.HasCount(15, rows);
        Assert.AreEqual("Moin", rows[0].Values["PortOfExit"]);
        Assert.AreEqual("9025", rows[0].Values["OceanFreight"]);
        Assert.AreEqual("9325", rows[1].Values["OceanFreight"]);
        Assert.AreEqual("10590", rows[0].Values["TotalSale"]);
        Assert.AreEqual("10890", rows[1].Values["TotalSale"]);
    }

    [TestMethod]
    public async Task AlonsoViaPanama_KeepsBalboaAsMaritimeDischargeAndTerrestrialLeg()
    {
        var rows = (await Extract("PUERTOS BASE_ FCL_CHINA-VIA PANAMA.pdf", Alonso("BALBOA")))
            .Tables.Single().Rows.ToArray();
        Assert.HasCount(15, rows);
        Assert.AreEqual("Balboa", rows[0].Values["PortOfExit"]);
        Assert.AreEqual("San José", rows[0].Values["DestinationPort"]);
        Assert.AreEqual("MAERSK", rows[0].Values["Carrier"]);
        Assert.AreEqual("6435", rows[0].Values["OceanFreight"]);
        Assert.AreEqual("2585", rows[0].Values["Surcharges"]);
        Assert.AreEqual("850", rows[0].Values["DestinationCharges"]);
        Assert.AreEqual("9870", rows[0].Values["TotalSale"]);
        StringAssert.Contains(rows[0].Values["Remarks"]!, "Multimodal");
    }

    [TestMethod]
    public async Task Logica_UsesVatExcludedSubtotalAndDoesNotInventCarrierOrStartDate()
    {
        var doc = await Extract("COT-10-0525 - Opcion A.pdf", Logica());
        var rows = doc.Tables.Single().Rows.ToArray();
        Assert.HasCount(1, rows);
        var row = rows[0].Values;
        Assert.AreEqual("Ningbo", row["OriginPort"]);
        Assert.AreEqual("Caldera", row["PortOfExit"]);
        Assert.AreEqual("Heredia", row["DestinationPort"]);
        Assert.AreEqual("40HC", row["ContainerType"]);
        Assert.AreEqual("7260", row["OceanFreight"]);
        Assert.AreEqual("847.8", row["DestinationCharges"]);
        Assert.AreEqual("695", row["Surcharges"]);
        Assert.AreEqual("8802.8", row["TotalSale"]);
        Assert.IsNull(row["Carrier"]);
        Assert.IsNull(row["ValidFrom"]);
        Assert.AreEqual("2026-10-21", row["ValidTo"]);
        Assert.AreEqual("true", row["RequiresManualReview"]);
        Assert.IsNull(PricingDocumentDefaultsEnricher.Enrich(doc)
            .Tables.Single().Rows.First().Values["ValidFrom"]);
    }

    [TestMethod]
    public async Task OtherPdf_FallsBackToOriginalExtractor()
    {
        var pdf = MakePdf([new(100, 700, "INTERNAL INVOICE")]);
        var result = await new PdfDocumentExtractor().ExtractAsync(
            new DocumentExtractionInput("other.pdf", "application/pdf", ".pdf", pdf));
        Assert.IsFalse(result.Tables.Any(table =>
            (table.SheetName ?? "").StartsWith("PDF Competitor", StringComparison.Ordinal)));
    }

    private static async Task<ExtractedDocument> Extract(string fileName, byte[] pdf) =>
        await new PdfDocumentExtractor().ExtractAsync(
            new DocumentExtractionInput(fileName, "application/pdf", ".pdf", pdf));

    private static byte[] Mgf(string destination, string from, string freight20,
        string freight40, string carrier = "YML")
    {
        var lines = new List<Placement>
        {
            new(80, 755, "Tarifario FCL Asia"),
            new(90, 705, "CARRIER"),
            new(155, 705, "POL"),
            new(240, 705, "POD"),
            new(100, 675, carrier),
            new(136, 675, "QINGDAO/NINGBO/YANTIAN/"),
            new(150, 665, "SHANGHAI/BUSAN"),
            new(242, 660, destination),
            new(271, 670, "$" + freight20),
            new(311, 670, "$" + freight40),
            new(400, 670, "10"),
            new(432, 670, "$915.00"),
            new(482, 670, "$355.00"),
            new(90, 490, $"Valida: Del {from} al 21/10/2026")
        };
        if (carrier == "EMC")
        {
            lines[3] = new Placement(240, 705, "POD");
            lines[5] = new Placement(138, 675, "HONG KONG/SHANGHAI/");
            lines[6] = new Placement(149, 665, "NINGBO/YANTIAN");
            lines[9] = new Placement(311, 670, "$" + freight40);
            lines[8] = new Placement(271, 670, "$" + freight20);
            lines[11] = new Placement(432, 670, "$1,005.00");
            lines[12] = new Placement(482, 670, "$480.00");
        }
        return MakePdf(lines, 6);
    }

    private static byte[] Alonso(string destination)
    {
        var panama = destination == "BALBOA";
        var moin = destination == "MOIN";
        var lines = new List<Placement>
        {
            new(80, 750, "Tarifa: TARIFARIOS PUERTOS BASE"),
            new(80, 735, "Fecha: 13/10/2026"),
            new(80, 720, "POL: SHENZHEN/XIAMEN/NINGBO/SHANGHAI/QINGDAO"),
            new(80, 705, "POD: " + destination),
            new(80, 690, "Servicio: FCL   Incoterm: FOB"),
            new(80, 675, panama ? "NAVIERA: MSK" : "NAVIERA: COSCO (VIA CHANCAY)"),
            new(80, 660, "VALIDITY 21/10/2026 21/10/2026"),
            new(80, 645, "DIAS LIBRES DESTINO DE CONTENEDOR " + (panama ? "10" : "7") + " DIAS"),
            new(80, 630, "Tiempo de transito maritimo: 45-60 dias"),
            new(80, 615, "EQUIPO: 1 X 20 STD   1 X 40 STD / HC")
        };
        if (panama)
        {
            lines.AddRange([
                new(80, 590, "FLETE MARITIMO $6 435,00 $6 685,00"),
                new(80, 575, "FLETE TERRESTRE PANAMA - COSTA RICA $2 585,00 $2 585,00"),
                new(80, 560, "GASTOS NAVIERA PANAMA $525,00 $525,00"),
                new(80, 545, "DOCUMENTACION EXPORTACION TERRESTRE PAN - CR $250,00 $250,00"),
                new(80, 530, "MANEJOS ALONSO $75,00 $75,00"),
                new(80, 515, "TOTAL USD $9 870,00 $10 120,00")
            ]);
        }
        else if (moin)
        {
            lines.AddRange([
                new(80, 590, "FLETE MARITIMO $9 025,00 $9 325,00"),
                new(80, 575, "THC $350,00 $350,00"),
                new(80, 560, "GASTOS NAVIERA $260,00 $260,00"),
                new(80, 545, "FLETE INTERNO $955,00 $955,00"),
                new(80, 530, "TOTAL USD $10 590,00 $10 890,00")
            ]);
        }
        else
        {
            lines.AddRange([
                new(80, 590, "FLETE MARITIMO INTERNACIONAL"),
                new(80, 575, "FLETE MARITIMO $6 425,00 $6 925,00"),
                new(80, 560, "THC $275,00 $275,00"),
                new(80, 545, "GASTOS NAVIERA $305,00 $305,00"),
                new(80, 530, "FLETE INTERNO $720,00 $720,00"),
                new(80, 515, "GATE $240,00 $240,00"),
                new(80, 500, "TOTAL USD $7 965,00 $8 465,00")
            ]);
        }
        return MakePdf(lines);
    }

    private static byte[] Logica() => MakePdf([
        new(80, 750, "COT-10-0525"),
        new(80, 735, "COTIZACION DE SERVICIOS"),
        new(80, 720, "ORIGEN NINGBO"),
        new(80, 705, "DESTINO PUERTO CALDERA/ALMACEN FISCAL HEREDIA"),
        new(80, 690, "FECHA 13 de octubre de 2026"),
        new(80, 675, "VIGENCIA 21 de octubre de 2026"),
        new(80, 660, "INCOTERM FOB EQUIPO 40 HQ"),
        new(80, 615, "1 FLETE MARITIMO $7,260.00"),
        new(80, 600, "2 DOCUMENTACION +IVA $35.00"),
        new(80, 585, "3 CARGOS DE NAVIERA +IVA $595.00"),
        new(80, 570, "4 FLETE INTERNO CALDERA-GAM +IVA $695.00"),
        new(80, 555, "5 CARGO SOBRE TRANSPORTE (COLL FEE) +IVA $217.80"),
        new(80, 530, "Subtotal $8,802.80"),
        new(80, 515, "IVA 13% $200.56"),
        new(80, 500, "TOTAL $9,003.36")
    ]);

    private sealed record Placement(int X, int Y, string Text);

    // A tiny standards-compliant, no external dependency PDF fixture writer.
    private static byte[] MakePdf(IEnumerable<Placement> placements, int fontSize = 9)
    {
        static string Escape(string text) => text.Replace("\\", "\\\\")
            .Replace("(", "\\(").Replace(")", "\\)");
        var stream = string.Join("\n", placements.Select(p =>
            $"BT /F1 {fontSize} Tf 1 0 0 1 {p.X} {p.Y} Tm ({Escape(p.Text)}) Tj ET")) + "\n";
        var streamBytes = Encoding.Latin1.GetBytes(stream);
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {streamBytes.Length} >>\nstream\n{stream}endstream"
        };
        var result = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int> { 0 };
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(Encoding.Latin1.GetByteCount(result.ToString()));
            result.Append(i + 1).Append(" 0 obj\n")
                .Append(objects[i]).Append("\nendobj\n");
        }
        var xref = Encoding.Latin1.GetByteCount(result.ToString());
        result.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            result.Append(offset.ToString("D10", CultureInfo.InvariantCulture))
                .Append(" 00000 n \n");
        result.Append("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n")
            .Append(xref.ToString(CultureInfo.InvariantCulture))
            .Append("\n%%EOF\n");
        return Encoding.Latin1.GetBytes(result.ToString());
    }
}
