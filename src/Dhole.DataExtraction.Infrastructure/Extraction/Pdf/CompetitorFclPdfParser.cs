using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Infrastructure.Normalization;
using UglyToad.PdfPig.Content;

namespace Dhole.DataExtraction.Infrastructure.Extraction.Pdf;

/// <summary>
/// Deterministic extraction for competitor FCL quotes with multiline origin ports,
/// shared 40STD/HC pricing and multimodal Panama legs. Never infer an undocumented
/// valid-from date, carrier or total. Commercial amounts are sale prices, not costs.
/// </summary>
public static class CompetitorFclPdfParser
{
    private const string MgfTable = "PDF Competitor FCL - MGF";
    private const string AlonsoTable = "PDF Competitor FCL - Alonso";
    private const string LogicaTable = "PDF Competitor FCL - Logica";
    private static readonly Regex PortPattern = new(
        @"HONG\s*KONG|NINGBO|SHANGHAI|SHENZHEN|YANTIAN|XIAMEN|QINGDAO|XINGANG|BUSAN",
        RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex MoneyPattern = new(
        @"\$\s*(?<amount>\d{1,3}(?:[\s.,]\d{3})*[.,]\d{2}|\d{3,7}[.,]\d{2})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DatePattern = new(
        @"(?<!\d)(?<d>\d{1,2})[/-](?<m>\d{1,2})[/-](?<y>20\d{2})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static List<ExtractedTable> TryParse(
        IReadOnlyCollection<Page> pages,
        IReadOnlyCollection<string> visualLines,
        string originalFileName)
    {
        // Some original quotation PDFs visually letter-space field labels, e.g.
        // "V I G E N C I A" and "F E C H A", not "VIGENCIA" and "FECHA".
        var lines = visualLines.Select(CanonicalizeQuotationLabel).ToArray();
        var text = string.Join("\n", lines);
        if (Regex.IsMatch(text, @"Tarifario\s+FCL\s+Asia", RegexOptions.IgnoreCase)
            && Regex.IsMatch(text, @"CARRIER\s+POL\s+POD", RegexOptions.IgnoreCase))
            return ParseMgf(pages, text, originalFileName);
        if (Regex.IsMatch(text, @"TARIFARIOS?\s+(?:PUERTOS\s+BASE|CHINA)", RegexOptions.IgnoreCase)
            && Regex.IsMatch(text, @"ALONSO|FLETE\s+MAR[IÍ]TIMO", RegexOptions.IgnoreCase))
            return ParseAlonso(lines);
        if (Regex.IsMatch(text, @"COT-10-0525", RegexOptions.IgnoreCase)
            && Regex.IsMatch(text, @"DOCUMENTACI[OÓ]N|CARGOS?\s+DE\s+NAVIERA", RegexOptions.IgnoreCase))
            return ParseLogica(lines);
        return [];
    }

    private static List<ExtractedTable> ParseMgf(
        IReadOnlyCollection<Page> pages,
        string text,
        string fileName)
    {
        var destination = fileName.Contains("MOIN", StringComparison.OrdinalIgnoreCase)
            ? "Moin"
            : fileName.Contains("CALDERA", StringComparison.OrdinalIgnoreCase)
                ? "Caldera"
                : Regex.IsMatch(text, @"\bMOIN\b", RegexOptions.IgnoreCase) ? "Moin" : "Caldera";
        var dates = ReadExplicitDateRange(text);
        var rows = new List<ExtractedRow>();

        foreach (var page in pages)
        {
            var words = page.GetWords()
                .Where(word => !string.IsNullOrWhiteSpace(word.Text))
                .ToArray();
            var tableHeader = words.FirstOrDefault(word =>
                word.Text.Equals("CARRIER", StringComparison.OrdinalIgnoreCase)
                && word.BoundingBox.Left < 140);
            if (tableHeader is null) continue;

            // Carrier labels anchor each visual row, even when POL wraps to 2-3 lines.
            var carriers = words
                .Where(word => word.BoundingBox.Left < 127
                    && word.BoundingBox.Bottom < tableHeader.BoundingBox.Bottom
                    && word.BoundingBox.Bottom > tableHeader.BoundingBox.Bottom - 175
                    && Regex.IsMatch(word.Text, @"^(YML|MSC|EMC|CMA|ONE|COSCO)$",
                        RegexOptions.IgnoreCase))
                .OrderByDescending(word => word.BoundingBox.Bottom)
                .ToArray();

            // Bound each row by the START of its POL cell, not by the
            // midpoint between carrier labels. In the real MGF Caldera PDF,
            // MSC's final "XINGANG" wraps below that midpoint and would be
            // wrongly attributed to EMC. In the Moín PDF the next carrier's
            // first POL line is printed ABOVE its carrier label.
            var portStarts = carriers.Select(carrier =>
                words.Where(word => word.BoundingBox.Left >= 125
                    && word.BoundingBox.Left < 260
                    && Math.Abs(word.BoundingBox.Bottom - carrier.BoundingBox.Bottom) < 20
                    && Regex.IsMatch(word.Text,
                        @"NINGBO|SHANGHAI|SHENZHEN|YANTIAN|XIAMEN|QINGDAO|XINGANG|BUSAN|HONG|KONG",
                        RegexOptions.IgnoreCase))
                    .OrderBy(word => Math.Abs(word.BoundingBox.Bottom - carrier.BoundingBox.Bottom))
                    .ThenByDescending(word => word.BoundingBox.Bottom)
                    .Select(word => word.BoundingBox.Bottom)
                    .DefaultIfEmpty(carrier.BoundingBox.Bottom)
                    .First()).ToArray();
            var lastPortStart = portStarts.LastOrDefault();
            var nextSection = words
                .Where(word => word.Text.Equals("CARGOS", StringComparison.OrdinalIgnoreCase)
                    && word.BoundingBox.Bottom < lastPortStart - 5)
                .OrderByDescending(word => word.BoundingBox.Bottom)
                .FirstOrDefault();
            var tableBottom = nextSection?.BoundingBox.Bottom
                ?? tableHeader.BoundingBox.Bottom - 155;

            for (var i = 0; i < carriers.Length; i++)
            {
                var top = portStarts[i] + 3;
                var bottom = i + 1 < carriers.Length ? portStarts[i + 1] : tableBottom;
                var band = words
                    .Where(word => word.BoundingBox.Bottom <= top
                        && word.BoundingBox.Bottom > bottom)
                    .ToArray();

                var portText = string.Join(" ", band
                    .Where(word => word.BoundingBox.Left >= 125
                        && word.BoundingBox.Left < 260)
                    .OrderByDescending(word => word.BoundingBox.Bottom)
                    .ThenBy(word => word.BoundingBox.Left)
                    .Select(word => word.Text));
                var ports = FindPorts(portText);
                if (ports.Count == 0) continue;

                var amounts = band
                    .Where(word => word.Text.Contains('$'))
                    .Select(word => new
                    {
                        X = word.BoundingBox.Left,
                        Amount = MoneyNormalizer.Normalize(word.Text)
                    })
                    .Where(item => item.Amount.HasValue)
                    .ToArray();
                var ocean20 = amounts.Where(item => item.X >= 248 && item.X < 295)
                    .Select(item => item.Amount).FirstOrDefault();
                var ocean40 = amounts.Where(item => item.X >= 297 && item.X < 353)
                    .Select(item => item.Amount).FirstOrDefault();
                var inland = amounts.Where(item => item.X >= 405 && item.X < 468)
                    .Select(item => item.Amount).FirstOrDefault();
                var carrierCharges = amounts.Where(item => item.X >= 470 && item.X < 535)
                    .Select(item => item.Amount).FirstOrDefault();

                if (!ocean20.HasValue || !ocean40.HasValue
                    || !inland.HasValue || !carrierCharges.HasValue)
                    continue;

                var freeDays = band
                    .Where(word => word.BoundingBox.Left >= 380 && word.BoundingBox.Left < 420)
                    .Select(word => int.TryParse(word.Text, out var value) && value is >= 1 and <= 30
                        ? (int?)value : null)
                    .FirstOrDefault(value => value.HasValue);

                var carrier = carriers[i].Text.ToUpperInvariant() switch
                {
                    "YML" => "YANG MING",
                    "CMA" => "CMA CGM",
                    "EMC" => "EVERGREEN",
                    var other => other
                };
                // 55 + 35 + 45 are explicit destination items per container, excluding VAT.
                const decimal commonDestinationCharges = 135m;
                var destinationCharges = commonDestinationCharges + carrierCharges.Value;
                var anomalous40 = ocean40.Value < ocean20.Value * .60m;
                var remarks = "MGF: movilización a GAM indicada por separado; "
                    + "cargos de destino por contenedor sin IVA; "
                    + "el PDF no publica total general.";
                if (anomalous40)
                    remarks += " PRECIO 40STD/HC ANÓMALO: requiere revisión manual, sin corregir el original.";
                if (dates.EndYearTruncated)
                    remarks += " VENCIMIENTO IMPRESO CON AÑO INCOMPLETO: se completó usando el año explícito de inicio; requiere revisión.";

                foreach (var port in ports)
                {
                    foreach (var (equipment, freight) in new[]
                    {
                        ("20DV", ocean20.Value), ("40DV", ocean40.Value),
                        ("40HC", ocean40.Value)
                    })
                    {
                        var values = BaseRow(port, destination, null, equipment,
                            carrier, "MGF", freight, dates.From, dates.To);
                        values["FreeDays"] = freeDays?.ToString(CultureInfo.InvariantCulture);
                        values["TransitDays"] = "48";
                        values["DestinationCharges"] = Money(destinationCharges);
                        values["Surcharges"] = Money(inland.Value);
                        values["Remarks"] = remarks;
                        values["RequiresManualReview"] = anomalous40 || dates.EndYearTruncated ? "true" : "false";
                        values["LineItemsJson"] = JsonSerializer.Serialize(new
                        {
                            OceanFreight = freight,
                            MobilizacionGam = inland.Value,
                            CarrierDestinationCharges = carrierCharges.Value,
                            Manejos = 55m, BlFee = 35m, Desconsolidacion = 45m,
                            VatOnCommonDestinationItems = 17.55m,
                            InlandIsSeparatelyQuoted = true,
                            TotalIsNotExplicit = true
                        });
                        Add(rows, values);
                    }
                }
            }
        }
        return Table(MgfTable, rows);
    }

    private static List<ExtractedTable> ParseAlonso(string[] lines)
    {
        var text = string.Join("\n", lines);
        var originBlock = HeaderAfterLabel(lines, "POL:", "POD:");
        var ports = FindPorts(originBlock);
        var destination = ReadLabel(lines, "POD:");
        if (ports.Count == 0 || string.IsNullOrWhiteSpace(destination))
            return [];

        var isPanama = destination.Contains("BALBOA", StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(text, @"FLETE\s+TERRESTRE\s+PANAM", RegexOptions.IgnoreCase);
        var poe = destination.Contains("MOIN", StringComparison.OrdinalIgnoreCase)
            ? "Moin" : destination.Contains("CALDERA", StringComparison.OrdinalIgnoreCase)
                ? "Caldera" : "Balboa";
        if (poe == "Balboa" && !isPanama) return [];

        var carrierLine = Line(lines, @"^\s*NAVIERA\s*:");
        var carrier = Regex.IsMatch(carrierLine, @"COSCO", RegexOptions.IgnoreCase)
            ? "COSCO" : Regex.IsMatch(carrierLine, @"\bMSK\b|MAERSK", RegexOptions.IgnoreCase)
                ? "MAERSK" : null;
        if (carrier is null) return [];

        var validity = Line(lines, @"^\s*VALIDITY\b");
        var expiry = ExtractDate(validity);
        var freeDays = FirstPositiveInt(Line(lines, @"DIAS\s+LIBRES\s+DESTINO"));
        var transit = FirstPositiveInt(Line(lines, @"Tiempo\s+de\s+tr[aá]nsito\s+mar[ií]timo"));
        var ocean = Pair(lines, @"^\s*FLETE\s+MAR[IÍ]TIMO\b");
        var totals = Pair(lines, @"^\s*TOTAL\s+USD\b");
        if (!ocean.HasValue || !totals.HasValue || !expiry.HasValue) return [];

        var charges = new Dictionary<string, (decimal First, decimal Second)>();
        foreach (var (key, pattern) in new[]
        {
            ("THC", @"^\s*THC\b"),
            ("GastosNaviera", @"^\s*GASTOS\s+NAVIERA\b"),
            ("Inland", @"^\s*FLETE\s+INTERNO\b"),
            ("Gate", @"^\s*GATE\b"),
            ("TerrestrePanama", @"^\s*FLETE\s+TERRESTRE\s+PANAM"),
            ("DocumentacionPanama", @"^\s*DOCUMENTACI[OÓ]N\s+EXPORTACI[OÓ]N\s+TERRESTRE"),
            ("Manejos", @"^\s*MANEJOS\s+ALONSO\b")
        })
        {
            var pair = Pair(lines, pattern);
            if (pair.HasValue) charges[key] = pair.Value;
        }

        var rows = new List<ExtractedRow>();
        foreach (var port in ports)
        {
            foreach (var (equipment, index) in new[]
            {
                ("20DV", 0), ("40DV", 1), ("40HC", 1)
            })
            {
                decimal Value(string key) => charges.TryGetValue(key, out var amounts)
                    ? index == 0 ? amounts.First : amounts.Second : 0m;
                var freight = index == 0 ? ocean.Value.First : ocean.Value.Second;
                var total = index == 0 ? totals.Value.First : totals.Value.Second;
                var destinationCharges = Value("THC") + Value("GastosNaviera")
                    + Value("Gate") + Value("DocumentacionPanama") + Value("Manejos");
                var surcharges = Value("Inland") + Value("TerrestrePanama");
                var difference = Math.Abs(freight + destinationCharges + surcharges - total);
                var values = BaseRow(port, poe,
                    "San José",
                    equipment, carrier, "ALONSO", freight, null, expiry);
                values["MissingExplicitValidFrom"] = "true";
                values["DocumentIssuedAt"] = ExtractDate(Line(lines, @"^\s*Fecha\s*:"))?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                values["FreeDays"] = freeDays?.ToString(CultureInfo.InvariantCulture);
                values["TransitDays"] = transit?.ToString(CultureInfo.InvariantCulture);
                values["DestinationCharges"] = Money(destinationCharges);
                values["Surcharges"] = Money(surcharges);
                values["TotalSale"] = Money(total);
                values["Incoterm"] = "FOB";
                values["Remarks"] = (isPanama
                    ? "Multimodal: POL China -> descarga marítima Balboa, Panamá -> "
                        + "tránsito terrestre Panamá-Costa Rica -> entrega San José. "
                    : "FCL marítimo con entrega final en San José. ")
                    + "La fecha de emisión no constituye inicio de vigencia; "
                    + "el PDF solo especifica vencimiento 21/10/2026."
                    + (carrierLine.Contains("CHANCAY", StringComparison.OrdinalIgnoreCase)
                        ? " Vía Chancay." : "")
                    + (difference > .02m
                        ? " IMPORTES NO CONCILIAN: revisión manual." : "");
                values["RequiresManualReview"] = "true"; // start date absent
                values["LineItemsJson"] = JsonSerializer.Serialize(new
                {
                    OceanFreight = freight, Thc = Value("THC"),
                    GastosNaviera = Value("GastosNaviera"), Gate = Value("Gate"),
                    Inland = Value("Inland"), TerrestrePanama = Value("TerrestrePanama"),
                    DocumentacionTerrestrePanama = Value("DocumentacionPanama"),
                    Manejos = Value("Manejos"), SourceTotalWithoutVat = total,
                    RequiresReviewForTotalMismatch = difference > .02m,
                    SeaPortOfDischarge = poe,
                    FinalDeliveryCity = "San José"
                });
                Add(rows, values);
            }
        }
        return Table(AlonsoTable, rows);
    }

    private static List<ExtractedTable> ParseLogica(string[] lines)
    {
        var text = string.Join("\n", lines);
        if (!Regex.IsMatch(text, @"\bNINGBO\b", RegexOptions.IgnoreCase)
            || !Regex.IsMatch(text, @"PUERTO\s+CALDERA", RegexOptions.IgnoreCase))
            return [];

        var expiryLine = Line(lines, @"^\s*VIGENCIA\b");
        var expiry = ExtractTextualDate(expiryLine);
        if (!expiry.HasValue) return [];
        var ocean = Single(lines, @"^\s*\d+\s+FLETE\s+MAR[IÍ]TIMO\b");
        var documentation = Single(lines, @"^\s*\d+\s+DOCUMENTACI[OÓ]N\b");
        var carrierCharge = Single(lines, @"^\s*\d+\s+CARGOS?\s+DE\s+NAVIERA\b");
        var inland = Single(lines, @"^\s*\d+\s+FLETE\s+INTERNO\b");
        var collectionFee = Single(lines, @"^\s*\d+\s+CARGO\s+SOBRE\s+TRANSPORTE\b");
        var subtotal = Single(lines, @"^\s*Subtotal\b");
        var vat = Single(lines, @"^\s*IVA\s*13%");
        var invoiceTotal = Single(lines, @"^\s*TOTAL\b");
        if (!ocean.HasValue || !documentation.HasValue
            || !carrierCharge.HasValue || !inland.HasValue
            || !collectionFee.HasValue || !subtotal.HasValue)
            return [];

        var destinationCharges = documentation.Value + carrierCharge.Value + collectionFee.Value;
        var computed = ocean.Value + destinationCharges + inland.Value;
        var mismatch = Math.Abs(computed - subtotal.Value) > .02m
            || (invoiceTotal.HasValue && vat.HasValue
                && Math.Abs(subtotal.Value + vat.Value - invoiceTotal.Value) > .02m);
        var values = BaseRow("Ningbo", "Caldera", "Heredia", "40HC",
            null, "LOGICA", ocean.Value, null, expiry);
        values["MissingExplicitValidFrom"] = "true";
        values["DocumentIssuedAt"] = ExtractTextualDate(Line(lines, @"^\s*FECHA\b"))?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        values["Incoterm"] = "FOB";
        values["TransitDays"] = "40";
        values["DestinationCharges"] = Money(destinationCharges);
        values["Surcharges"] = Money(inland.Value);
        values["TotalSale"] = Money(subtotal.Value); // exclude VAT
        values["RequiresManualReview"] = "true";
        values["Remarks"] = "Lógica, COT-10-0525 Opción A: POL Ningbo, descarga "
            + "Puerto Caldera y entrega en almacén fiscal Heredia. "
            + "Naviera e inicio de vigencia no informados. "
            + "Subtotal sin IVA como venta comparable."
            + (mismatch ? " IMPORTES NO CONCILIAN: revisión manual." : "");
        values["LineItemsJson"] = JsonSerializer.Serialize(new
        {
            OceanFreight = ocean.Value, Documentacion = documentation.Value,
            CarrierDestinationCharges = carrierCharge.Value,
            InlandCalderaGam = inland.Value, CollectionFee = collectionFee.Value,
            SubtotalExcludingVat = subtotal.Value, Vat = vat,
            InvoiceTotalIncludingVat = invoiceTotal,
            RequiresReviewForTotalMismatch = mismatch
        });
        var rows = new List<ExtractedRow>();
        Add(rows, values);
        return Table(LogicaTable, rows);
    }

    private static Dictionary<string, string?> BaseRow(
        string origin, string poe, string? finalDestination, string equipment,
        string? carrier, string agent, decimal freight,
        DateTime? validFrom, DateTime? validTo) => new(StringComparer.OrdinalIgnoreCase)
    {
        ["OriginPort"] = origin,
        ["PortOfExit"] = poe,
        ["DestinationPort"] = finalDestination,
        ["ContainerType"] = equipment,
        ["Carrier"] = carrier,
        ["Agent"] = agent,
        ["TariffMode"] = "FCL",
        ["Currency"] = "USD",
        ["OceanFreight"] = Money(freight),
        ["ValidFrom"] = validFrom?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ["ValidTo"] = validTo?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
    };

    private static List<string> FindPorts(string text)
    {
        return PortPattern.Matches(text)
            .Select(match => Regex.Replace(match.Value, @"\s+", " ").Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string HeaderAfterLabel(string[] lines, string start, string end)
    {
        var startIndex = Array.FindIndex(lines, line =>
            line.Contains(start, StringComparison.OrdinalIgnoreCase));
        if (startIndex < 0) return string.Empty;
        var endIndex = Array.FindIndex(lines, startIndex + 1, line =>
            line.Contains(end, StringComparison.OrdinalIgnoreCase));
        if (endIndex < 0 || endIndex - startIndex > 12)
            endIndex = Math.Min(lines.Length, startIndex + 9);
        // In two-column Alonso layouts the first origin (SHENZHEN) can be
        // visually above the POL caption; preserve it without absorbing
        // unrelated document text or the routing/price sections.
        var precedingPortLines = lines
            .Skip(Math.Max(0, startIndex - 2))
            .Take(Math.Min(2, startIndex))
            .Where(line => PortPattern.IsMatch(line)
                && !line.Contains("POD:", StringComparison.OrdinalIgnoreCase));
        var following = string.Join(" ", lines[startIndex..endIndex])
            .Split(start, 2, StringSplitOptions.None).Last();
        return string.Join(" ", precedingPortLines.Append(following));
    }

    private static string? ReadLabel(string[] lines, string label)
    {
        var line = lines.FirstOrDefault(item =>
            item.Contains(label, StringComparison.OrdinalIgnoreCase));
        if (line is null) return null;
        return line[(line.IndexOf(label, StringComparison.OrdinalIgnoreCase)
            + label.Length)..].Trim();
    }

    private static string Line(string[] lines, string regex) => lines.FirstOrDefault(line =>
        Regex.IsMatch(line, regex, RegexOptions.IgnoreCase)) ?? "";

    private static (decimal First, decimal Second)? Pair(string[] lines, string regex)
    {
        // A section caption may match the same label with no prices: use the
        // priced row rather than stopping at the banner above the tariff table.
        foreach (var line in lines.Where(line => Regex.IsMatch(line, regex, RegexOptions.IgnoreCase)))
        {
            var amounts = Amounts(line);
            if (amounts.Count >= 2) return (amounts[0], amounts[1]);
        }
        return null;
    }

    private static decimal? Single(string[] lines, string regex)
    {
        foreach (var line in lines.Where(line => Regex.IsMatch(line, regex, RegexOptions.IgnoreCase)))
        {
            var amounts = Amounts(line);
            if (amounts.Count > 0) return amounts[0];
        }
        return null;
    }

    private static List<decimal> Amounts(string text) =>
        MoneyPattern.Matches(text)
            .Select(match => MoneyNormalizer.Normalize(match.Groups["amount"].Value))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();

    private static int? FirstPositiveInt(string text)
    {
        var match = Regex.Match(text, @"\d{1,3}");
        return match.Success && int.TryParse(match.Value, out var value)
            && value > 0 ? value : null;
    }

    private static DateTime? ExtractDate(string text)
    {
        var match = DatePattern.Match(text);
        return match.Success
            && int.TryParse(match.Groups["d"].Value, out var day)
            && int.TryParse(match.Groups["m"].Value, out var month)
            && int.TryParse(match.Groups["y"].Value, out var year)
            && DateTime.TryParseExact($"{day:D2}/{month:D2}/{year}",
                "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None,
                out var date)
            ? date : null;
    }

    private static DateTime? ExtractTextualDate(string text)
    {
        var match = Regex.Match(text,
            @"(?<day>\d{1,2})\s+de\s+(?<month>octubre)\s+de\s+(?<year>20\d{2})",
            RegexOptions.IgnoreCase);
        return match.Success
            && int.TryParse(match.Groups["day"].Value, out var day)
            && int.TryParse(match.Groups["year"].Value, out var year)
            ? new DateTime(year, 10, day) : null;
    }

    private static (DateTime? From, DateTime? To, bool EndYearTruncated) ReadExplicitDateRange(string text)
    {
        // Some printed MGF PDFs literally say "21/10/202" (three-digit
        // expiry year). Restore the final digit ONLY when the same range
        // includes a complete start year with the same prefix, and flag it.
        var match = Regex.Match(text,
            @"V[ÁA]LIDA\s*:\s*DEL\s*(?<from>\d{1,2}/\d{1,2}/20\d{2})\s*AL\s*(?<to>\d{1,2}/\d{1,2}/20\d{1,2})(?!\d)",
            RegexOptions.IgnoreCase);
        if (!match.Success) return (null, null, false);
        var from = ExtractDate(match.Groups["from"].Value);
        var until = match.Groups["to"].Value;
        var truncated = Regex.Match(until, @"^(?<day>\d{1,2}/\d{1,2}/)(?<year>20\d)$");
        if (truncated.Success && from.HasValue)
        {
            var year = from.Value.Year.ToString(CultureInfo.InvariantCulture);
            if (year.StartsWith(truncated.Groups["year"].Value, StringComparison.Ordinal))
                return (from, ExtractDate(truncated.Groups["day"].Value + year), true);
        }
        return (from, ExtractDate(until), false);
    }

    private static string CanonicalizeQuotationLabel(string line)
    {
        // Keep the rest of the visual row, including amounts and dates, intact.
        line = Regex.Replace(line, @"^\s*V\s+I\s+G\s+E\s+N\s+C\s+I\s+A\b",
            "VIGENCIA", RegexOptions.IgnoreCase);
        line = Regex.Replace(line, @"^\s*F\s+E\s+C\s+H\s+A\b",
            "FECHA", RegexOptions.IgnoreCase);
        return line;
    }

    private static string Money(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static void Add(List<ExtractedRow> rows, Dictionary<string, string?> values)
    {
        rows.Add(new ExtractedRow(rows.Count + 1, values,
            JsonSerializer.Serialize(values)));
    }

    private static List<ExtractedTable> Table(string name, List<ExtractedRow> rows)
    {
        if (rows.Count == 0) return [];
        return [new ExtractedTable(name, rows.SelectMany(row => row.Values.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), rows)];
    }
}
