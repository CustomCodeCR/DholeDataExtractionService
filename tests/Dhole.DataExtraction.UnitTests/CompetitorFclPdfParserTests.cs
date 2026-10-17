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
    public async Task MgfCaldera_FullMatrixYields108IndependentOriginEquipmentRates()
    {
        var rows = (await Extract("TARIFARIO MGF VALIDO DEL 16 AL 21 DE OCTUBRE_ CALDERA.pdf",
            FullMgf("CALDERA"))).Tables.Single().Rows.ToArray();
        Assert.HasCount(108, rows, string.Join("; ", rows.GroupBy(row => row.Values["Carrier"]).Select(group => $"{group.Key}: {string.Join(",", group.Select(x => x.Values["OriginPort"]).Distinct())}")));
        Assert.AreEqual(36, rows.Select(row => $"{row.Values["OriginPort"]}|{row.Values["Carrier"]}")
            .Distinct().Count());
        Assert.IsTrue(rows.All(row => row.Values["ValidFrom"] == "2026-10-16"));
    }

    [TestMethod]
    public async Task MgfMoin_FullMatrixYields96IndependentOriginEquipmentRates()
    {
        var rows = (await Extract("TARIFARIO MGF VALIDO DEL 15 AL 21 DE OCTUBRE_ MOIN.pdf",
            FullMgf("MOIN"))).Tables.Single().Rows.ToArray();
        Assert.HasCount(96, rows);
        Assert.AreEqual(32, rows.Select(row => $"{row.Values["OriginPort"]}|{row.Values["Carrier"]}")
            .Distinct().Count());
        Assert.IsTrue(rows.All(row => row.Values["ValidFrom"] == "2026-10-15"));
        Assert.IsTrue(rows.Any(row => row.Values["Carrier"] == "EVERGREEN"
            && row.Values["ContainerType"] == "40HC"
            && row.Values["OceanFreight"] == "1245"));
    }

    [TestMethod]
    public async Task RealMgfCaldera_WrappedMscXingangDoesNotLeakIntoEvergreen()
    {
        // The real MGF PDF wraps MSC's last POL below the carrier-label
        // midpoint, before the next EMC carrier cell begins.
        var rows = (await Extract("TARIFARIO MGF VALIDO DEL 16 AL 21 DE OCTUBRE_ CALDERA.pdf",
            MakePdf([
                new(80, 760, "Tarifario FCL Asia"),
                new(90, 705, "CARRIER"), new(155, 705, "POL"),
                new(240, 705, "POD"),
                new(100, 650, "MSC"),
                new(128, 650, "SHANGHAI/NINGBO/QINGDAO/HONG"),
                new(128, 635, "KONG/BUSAN/YANTIAN/XIAMEN/"),
                new(128, 622, "XINGANG"),
                new(271, 645, "$7,565.00"), new(311, 645, "$7,865.00"),
                new(400, 645, "10"), new(432, 645, "$815.00"),
                new(482, 645, "$495.00"),
                new(100, 616, "EMC"), new(128, 616, "HONG"),
                new(128, 607, "KONG/SHANGHAI/NINGBO/YANTIAN"),
                new(271, 614, "$7,015.00"), new(311, 614, "$7,265.00"),
                new(400, 614, "15"), new(432, 614, "$940.00"),
                new(482, 614, "$355.00"),
                new(90, 470, "Valida: Del 16/10/2026 al 21/10/2026")
            ], 6))).Tables.Single().Rows.ToArray();
        Assert.HasCount(36, rows);
        Assert.IsTrue(rows.Any(row => row.Values["Carrier"] == "MSC"
            && row.Values["OriginPort"] == "XINGANG"));
        Assert.IsFalse(rows.Any(row => row.Values["Carrier"] == "EVERGREEN"
            && row.Values["OriginPort"] == "XINGANG"));
        Assert.AreEqual(8, rows.Where(row => row.Values["Carrier"] == "MSC")
            .Select(row => row.Values["OriginPort"]).Distinct().Count());
        Assert.AreEqual(4, rows.Where(row => row.Values["Carrier"] == "EVERGREEN")
            .Select(row => row.Values["OriginPort"]).Distinct().Count());
    }

    [TestMethod]
    public async Task RealMgfMoin_IncompletePrintedYearUsesExplicitStartYearWithoutMassReview()
    {
        var rows = (await Extract("TARIFARIO MGF VALIDO DEL 15 AL 21 DE OCTUBRE_ MOIN.pdf",
            Mgf("MOIN", "15/10/2026", "9,865.00", "1,245.00",
                "EMC", "21/10/202"))).Tables.Single().Rows.ToArray();
        Assert.HasCount(12, rows);
        Assert.IsTrue(rows.All(row => row.Values["ValidFrom"] == "2026-10-15"
            && row.Values["ValidTo"] == "2026-10-21"));
        Assert.AreEqual(8, rows.Count(row => row.Values["RequiresManualReview"] == "true"));
        Assert.AreEqual(4, rows.Count(row => row.Values["RequiresManualReview"] == "false"));
        StringAssert.Contains(rows[0].Values["Remarks"]!, "AÑO INCOMPLETO");
        Assert.AreEqual("1245", rows[1].Values["OceanFreight"]);
    }

    [TestMethod]
    public async Task RealLogica_LetterSpacedValidityAndDateLabelsAreRead()
    {
        var rows = (await Extract("COT-10-0525 - Opción A.pdf",
            Logica(spacedLabels: true))).Tables.Single().Rows.ToArray();
        Assert.HasCount(1, rows);
        Assert.AreEqual("2026-10-21", rows[0].Values["ValidTo"]);
        Assert.IsNull(rows[0].Values["ValidFrom"]);
        Assert.AreEqual("8802.8", rows[0].Values["TotalSale"]);
        Assert.IsNull(rows[0].Values["Carrier"]);
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
        string freight40, string carrier = "YML", string to = "21/10/2026")
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
            new(90, 490, $"Valida: Del {from} al {to}")
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


    private static byte[] FullMgf(string destination)
    {
        var moin = destination == "MOIN";
        (string Carrier, string Origins, string Price20, string Price40, string Inland, string Naviera)[] entries = moin
            ? [
                ("YML", "YANTIAN/XIAMEN/QINGDAO/XINGANG/HONG KONG", "7,565.00", "8,715.00", "930.00", "455.00"),
                ("ONE", "SHANGHAI/NINGBO/SHENZHEN/QINGDAO/XIAMEN/HONG KONG/BUSAN", "9,115.00", "9,165.00", "1,225.00", "745.00"),
                ("MSC", "SHANGHAI/NINGBO/SHENZHEN/QINGDAO/XIAMEN/XINGANG/HONG KONG/BUSAN", "9,465.00", "9,665.00", "955.00", "620.00"),
                ("EMC", "HONG KONG/SHANGHAI/NINGBO/YANTIAN", "9,865.00", "1,245.00", "1,005.00", "480.00"),
                ("COSCO", "SHANGHAI/NINGBO/SHENZHEN/QINGDAO/XIAMEN/XINGANG/HONG KONG/BUSAN", "9,265.00", "9,465.00", "1,130.00", "555.00")
            ]
            : [
                ("YML", "QINGDAO/NINGBO/YANTIAN/SHANGHAI/BUSAN", "6,480.00", "7,395.00", "915.00", "355.00"),
                ("MSC", "SHANGHAI/NINGBO/QINGDAO/HONG KONG/BUSAN/YANTIAN/XIAMEN/XINGANG", "7,565.00", "7,865.00", "815.00", "495.00"),
                ("EMC", "HONG KONG/SHANGHAI/NINGBO/YANTIAN", "7,015.00", "7,265.00", "940.00", "355.00"),
                ("CMA", "QINGDAO/XINGANG/SHENZHEN/NINGBO", "6,615.00", "6,915.00", "765.00", "365.00"),
                ("ONE", "SHANGHAI/NINGBO/QINGDAO/HONG KONG/BUSAN/YANTIAN/XIAMEN", "6,735.00", "7,265.00", "755.00", "510.00"),
                ("COSCO", "SHANGHAI/NINGBO/QINGDAO/HONG KONG/BUSAN/YANTIAN/XIAMEN/XINGANG", "6,465.00", "6,765.00", "855.00", "430.00")
            ];
        var placements = new List<Placement>
        {
            new(90, 760, "Tarifario FCL Asia"),
            new(90, 705, "CARRIER"),
            new(155, 705, "POL"),
            new(240, 705, "POD")
        };
        for (var i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];
            var y = 675 - i * 25;
            placements.Add(new Placement(99, y, entry.Carrier));
            // Split at natural port boundaries to emulate the multiline POL cell.
            var ports = entry.Origins.Split('/');
            var half = (ports.Length + 1) / 2;
            // MGF Moín prints POL first lines above the carrier name; Caldera
            // aligns POL and carrier on the same text baseline.
            placements.Add(new Placement(128, moin ? y + 8 : y, string.Join("/", ports.Take(half)) + "/"));
            placements.Add(new Placement(128, y - 8, string.Join("/", ports.Skip(half))));
            placements.Add(new Placement(271, y, "$" + entry.Price20));
            placements.Add(new Placement(311, y, "$" + entry.Price40));
            placements.Add(new Placement(400, y, "10"));
            placements.Add(new Placement(432, y, "$" + entry.Inland));
            placements.Add(new Placement(482, y, "$" + entry.Naviera));
        }
        placements.Add(new Placement(90, 470,
            $"Valida: Del {(moin ? "15" : "16")}/10/2026 al 21/10/2026"));
        return MakePdf(placements, 6);
    }

    private static byte[] Alonso(string destination)
    {
        var panama = destination == "BALBOA";
        var moin = destination == "MOIN";
        var lines = new List<Placement>
        {
            new(80, 750, "Tarifa: TARIFARIOS PUERTOS BASE"),
            new(80, 735, "Fecha: 13/10/2026"),
            new(80, 720, moin ? "SHENZHEN" : "POL: SHENZHEN/XIAMEN/NINGBO/SHANGHAI/QINGDAO"),
            new(80, 711, moin ? "POL: /XIAMEN/NINGBO/SHANGHAI/QINGDAO" : ""),
            new(80, 700, "POD: " + destination),
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

    private static byte[] Logica(bool spacedLabels = false) => MakePdf([
        new(80, 750, "COT-10-0525"),
        new(80, 735, "COTIZACION DE SERVICIOS"),
        new(80, 720, "ORIGEN NINGBO"),
        new(80, 705, "DESTINO PUERTO CALDERA/ALMACEN FISCAL HEREDIA"),
        new(80, 690, spacedLabels ? "F E C H A 13 de octubre de 2026" : "FECHA 13 de octubre de 2026"),
        new(80, 675, spacedLabels ? "V I G E N C I A 21 de octubre de 2026" : "VIGENCIA 21 de octubre de 2026"),
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
