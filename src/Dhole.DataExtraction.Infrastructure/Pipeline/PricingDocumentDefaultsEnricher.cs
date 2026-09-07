using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Infrastructure.Mapping;
using Dhole.DataExtraction.Infrastructure.Normalization;

namespace Dhole.DataExtraction.Infrastructure.Pipeline;

internal static class PricingDocumentDefaultsEnricher
{
    private enum TariffMode
    {
        Fcl,
        Lcl,
        Ltl,
        Air,
    }

    private static readonly IReadOnlyDictionary<string, int> Months =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["enero"] = 1,
            ["january"] = 1,
            ["jan"] = 1,
            ["febrero"] = 2,
            ["february"] = 2,
            ["feb"] = 2,
            ["marzo"] = 3,
            ["march"] = 3,
            ["mar"] = 3,
            ["abril"] = 4,
            ["april"] = 4,
            ["apr"] = 4,
            ["mayo"] = 5,
            ["may"] = 5,
            ["junio"] = 6,
            ["june"] = 6,
            ["jun"] = 6,
            ["julio"] = 7,
            ["july"] = 7,
            ["jul"] = 7,
            ["agosto"] = 8,
            ["august"] = 8,
            ["aug"] = 8,
            ["septiembre"] = 9,
            ["setiembre"] = 9,
            ["september"] = 9,
            ["sep"] = 9,
            ["octubre"] = 10,
            ["october"] = 10,
            ["oct"] = 10,
            ["noviembre"] = 11,
            ["november"] = 11,
            ["nov"] = 11,
            ["diciembre"] = 12,
            ["december"] = 12,
            ["dec"] = 12,
        };

    private static readonly string[] PrimaryRateAliases =
    [
        "oceanfreight",
        "oceanfreightwm",
        "oceanfreightrate",
        "freightrate",
        "lclrate",
        "lclratewm",
        "wmrate",
        "ltlrate",
        "ltlratewm",
        "landfreightrate",
        "landfreightratekg",
        "flete",
        "tarifa",
        "rate",
        "100",
        "flete100",
        "rate100",
    ];

    private static readonly string[] MinimumRateAliases =
    [
        "ofrmin",
        "minimum",
        "minimo",
        "min",
        "landmin",
        "lfrmin",
        "ltlratemin",
    ];

    public static ExtractedDocument Enrich(ExtractedDocument document)
    {
        var context = BuildContext(document);
        var mode = DetectMode(document, context);
        var validity = TryInferValidity(document.OriginalFileName, out var fileFrom, out var fileTo)
            ? (From: fileFrom, To: fileTo)
            : TryInferValidity(context, out var contextFrom, out var contextTo)
                ? (From: contextFrom, To: contextTo)
                : ((DateTime From, DateTime To)?)null;
        var route = InferRoute(document, context);

        var tables = document.Tables
            .Select(table => EnrichTable(table, document, mode, context, validity, route))
            .ToList();

        if (mode != TariffMode.Fcl && !tables.SelectMany(x => x.Rows).Any(IsUsableGeneralizedRow))
        {
            var synthetic = BuildSyntheticTables(document, mode, context, validity, route);
            tables.AddRange(synthetic);
        }

        return document with { Tables = tables };
    }

    private static ExtractedTable EnrichTable(
        ExtractedTable table,
        ExtractedDocument document,
        TariffMode mode,
        string context,
        (DateTime From, DateTime To)? validity,
        (string? Origin, string? Destination) route
    )
    {
        var tableDefaultOrigin = InferTableDefaultOrigin(table);
        var rows = table.Rows
            .Select(row => EnrichRow(
                row,
                table,
                document,
                mode,
                context,
                validity,
                route,
                tableDefaultOrigin
            ))
            .ToArray();

        var headers = table.Headers.ToList();
        foreach (var syntheticHeader in new[]
        {
            "OriginPort",
            "PortOfExit",
            "ContainerType",
            "Carrier",
            "Currency",
            "ValidFrom",
            "ValidTo",
            "OceanFreight",
            "TariffMode",
            "RateBasis",
        })
        {
            if (rows.Any(x => x.Values.ContainsKey(syntheticHeader))
                && !headers.Contains(syntheticHeader, StringComparer.OrdinalIgnoreCase))
            {
                headers.Add(syntheticHeader);
            }
        }

        return table with { Headers = headers, Rows = rows };
    }

    private static ExtractedRow EnrichRow(
        ExtractedRow row,
        ExtractedTable table,
        ExtractedDocument document,
        TariffMode mode,
        string context,
        (DateTime From, DateTime To)? validity,
        (string? Origin, string? Destination) route,
        string? tableDefaultOrigin
    )
    {
        var values = new Dictionary<string, string?>(row.Values, StringComparer.OrdinalIgnoreCase);

        if (validity.HasValue)
        {
            SetIfMissing(values, "ValidFrom", validity.Value.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            SetIfMissing(values, "ValidTo", validity.Value.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        if (mode == TariffMode.Fcl)
        {
            return row with { Values = values, RawJson = JsonSerializer.Serialize(values) };
        }

        var origin = FirstText(
            Read(values, "OriginPort", "POL", "Origin", "Origen", "From"),
            tableDefaultOrigin,
            route.Origin,
            InferDirectionalDefault(document, table, context, wantOrigin: true)
        );
        var destination = FirstText(
            Read(values, "PortOfExit", "POE", "POD", "Destination", "Destino", "To"),
            route.Destination,
            InferDirectionalDefault(document, table, context, wantOrigin: false)
        );
        var carrier = FirstText(
            Read(values, "Carrier", "Naviera", "ShippingLine", "Aerolinea", "Aerolínea", "Airline"),
            mode switch
            {
                TariffMode.Air => "Aéreo",
                TariffMode.Ltl => "Terrestre / LTL",
                _ => "LCL Consolidado",
            }
        );
        var containerType = mode switch
        {
            TariffMode.Air => "AIR",
            TariffMode.Ltl => "LTL",
            _ => "LCL",
        };

        SetIfMissing(values, "OriginPort", origin);
        SetIfMissing(values, "PortOfExit", destination);
        SetIfMissing(values, "Carrier", carrier);
        SetIfMissing(values, "ContainerType", containerType);
        SetIfMissing(values, "Currency", InferCurrency(values, context, mode));
        SetIfMissing(values, "TariffMode", mode.ToString().ToUpperInvariant());
        SetIfMissing(values, "RateBasis", mode == TariffMode.Air ? "KG/VOL" : "W/M");

        if (MoneyNormalizer.Normalize(Read(values, "OceanFreight")) is null
            && TryResolvePrimaryRate(values, mode, out var primaryRate))
        {
            values["OceanFreight"] = primaryRate.ToString("0.####", CultureInfo.InvariantCulture);
        }

        return row with { Values = values, RawJson = JsonSerializer.Serialize(values) };
    }

    private static IReadOnlyCollection<ExtractedTable> BuildSyntheticTables(
        ExtractedDocument document,
        TariffMode mode,
        string context,
        (DateTime From, DateTime To)? validity,
        (string? Origin, string? Destination) route
    )
    {
        if (mode == TariffMode.Air)
        {
            var airRows = ParseAirRows(document.RawText ?? context, context, validity, route);
            return airRows.Count == 0
                ? []
                : [new ExtractedTable(
                    "AIR - normalizado",
                    ["OriginPort", "PortOfExit", "Carrier", "ContainerType", "Currency", "ValidFrom", "ValidTo", "OceanFreight", "MinimumRate", "TariffMode", "RateBasis"],
                    airRows
                )];
        }

        if (mode == TariffMode.Ltl)
        {
            var rate = ExtractWmRate(context);
            if (!rate.HasValue || validity is null)
            {
                return [];
            }

            var origin = FirstText(route.Origin, InferNamedOrigin(context));
            var destination = FirstText(route.Destination, InferNamedDestination(context));
            if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(destination))
            {
                return [];
            }

            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["OriginPort"] = origin,
                ["PortOfExit"] = destination,
                ["Carrier"] = "Terrestre / LTL",
                ["ContainerType"] = "LTL",
                ["Currency"] = InferCurrency([], context, mode),
                ["ValidFrom"] = validity.Value.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["ValidTo"] = validity.Value.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["OceanFreight"] = rate.Value.ToString("0.####", CultureInfo.InvariantCulture),
                ["TariffMode"] = "LTL",
                ["RateBasis"] = "W/M",
            };

            return [new ExtractedTable(
                "LTL - normalizado",
                values.Keys.ToArray(),
                [new ExtractedRow(1, values, JsonSerializer.Serialize(values))]
            )];
        }

        return [];
    }

    private static List<ExtractedRow> ParseAirRows(
        string text,
        string context,
        (DateTime From, DateTime To)? validity,
        (string? Origin, string? Destination) route
    )
    {
        if (validity is null)
        {
            return [];
        }

        var rows = new List<ExtractedRow>();
        var rowNumber = 1;
        foreach (var rawLine in Regex.Split(text, @"\r?\n"))
        {
            var line = Regex.Replace(rawLine, @"\s+", " ").Trim();
            if (line.Length < 5)
            {
                continue;
            }

            var moneyMatches = Regex.Matches(
                line,
                @"(?:US\$|USD\s*|EUR\s*|[$€])\s*\d+(?:[.,]\d+)?",
                RegexOptions.IgnoreCase
            );
            if (moneyMatches.Count < 2)
            {
                continue;
            }

            var minimum = MoneyNormalizer.Normalize(moneyMatches[0].Value);
            var rate = MoneyNormalizer.Normalize(moneyMatches[1].Value);
            if (!minimum.HasValue || !rate.HasValue)
            {
                continue;
            }

            var prefix = line[..moneyMatches[0].Index].Trim(' ', '-', ':');
            var carrier = ResolveAirCarrier(line, prefix, moneyMatches[1]);
            if (string.IsNullOrWhiteSpace(carrier) || LooksLikeAirChargeLabel(carrier))
            {
                continue;
            }

            var origin = FirstText(route.Origin, InferAirOrigin(line, context));
            var destination = FirstText(route.Destination, InferAirDestination(context));
            if (string.IsNullOrWhiteSpace(origin) || string.IsNullOrWhiteSpace(destination))
            {
                continue;
            }

            var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["OriginPort"] = origin,
                ["PortOfExit"] = destination,
                ["Carrier"] = carrier,
                ["ContainerType"] = "AIR",
                ["Currency"] = line.Contains('€') || Regex.IsMatch(line, @"\bEUR\b", RegexOptions.IgnoreCase) ? "EUR" : "USD",
                ["ValidFrom"] = validity.Value.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["ValidTo"] = validity.Value.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["OceanFreight"] = rate.Value.ToString("0.####", CultureInfo.InvariantCulture),
                ["MinimumRate"] = minimum.Value.ToString("0.####", CultureInfo.InvariantCulture),
                ["TariffMode"] = "AIR",
                ["RateBasis"] = "KG/VOL",
            };

            rows.Add(new ExtractedRow(rowNumber++, values, JsonSerializer.Serialize(values)));
        }

        return rows;
    }

    private static string? ResolveAirCarrier(string line, string prefix, Match secondAmount)
    {
        var cleanPrefix = Regex.Replace(prefix, @"^(?:RUTA\s*:?)?\s*(?:[A-Z]{3}|MIAMI)\s*$", string.Empty, RegexOptions.IgnoreCase).Trim();
        if (!string.IsNullOrWhiteSpace(cleanPrefix))
        {
            return cleanPrefix;
        }

        var suffix = line[(secondAmount.Index + secondAmount.Length)..].Trim();
        if (string.IsNullOrWhiteSpace(suffix))
        {
            return null;
        }

        var stop = Regex.Match(
            suffix,
            @"\b(?:TARIFA|SPOT|DIRECTO|DIRECT|SABADOS?|VIER\.?|SUJETO|SALIDAS?|CUT[- ]?OFF|TRANSITO|TRÁNSITO|SERVICIO)\b",
            RegexOptions.IgnoreCase
        );
        var candidate = (stop.Success ? suffix[..stop.Index] : suffix).Trim(' ', '-', '/');
        return string.IsNullOrWhiteSpace(candidate) ? null : candidate;
    }

    private static bool LooksLikeAirChargeLabel(string carrier)
    {
        var normalized = ColumnHeaderNormalizer.Normalize(carrier);
        return new[]
        {
            "awb", "awbfee", "screening", "screnning", "fuel", "handling", "transfer",
            "bonded", "bondedfee", "xray", "documentation", "desconsolidacion", "shipper",
            "gastoslocales", "costosfcamiami", "minimum", "minimo", "pickup",
        }.Any(normalized.Contains);
    }

    private static bool TryResolvePrimaryRate(
        IReadOnlyDictionary<string, string?> values,
        TariffMode mode,
        out decimal rate
    )
    {
        var aliases = mode == TariffMode.Air
            ? PrimaryRateAliases.OrderBy(x => x is "100" or "flete100" or "rate100" ? 0 : 1)
            : PrimaryRateAliases;

        foreach (var alias in aliases)
        {
            var raw = ReadByNormalizedKey(values, alias);
            var parsed = MoneyNormalizer.Normalize(raw);
            if (parsed.HasValue && parsed.Value >= 0m)
            {
                rate = parsed.Value;
                return true;
            }
        }

        foreach (var alias in MinimumRateAliases)
        {
            var raw = ReadByNormalizedKey(values, alias);
            var parsed = MoneyNormalizer.Normalize(raw);
            if (parsed.HasValue && parsed.Value >= 0m)
            {
                rate = parsed.Value;
                return true;
            }
        }

        rate = 0m;
        return false;
    }

    private static bool IsUsableGeneralizedRow(ExtractedRow row)
    {
        return !string.IsNullOrWhiteSpace(Read(row.Values, "OriginPort"))
            && !string.IsNullOrWhiteSpace(Read(row.Values, "PortOfExit"))
            && !string.IsNullOrWhiteSpace(Read(row.Values, "Carrier"))
            && !string.IsNullOrWhiteSpace(Read(row.Values, "ContainerType"))
            && !string.IsNullOrWhiteSpace(Read(row.Values, "ValidFrom"))
            && !string.IsNullOrWhiteSpace(Read(row.Values, "ValidTo"))
            && MoneyNormalizer.Normalize(Read(row.Values, "OceanFreight")) is not null;
    }

    private static TariffMode DetectMode(ExtractedDocument document, string context)
    {
        var normalized = ColumnHeaderNormalizer.Normalize(context);
        var file = ColumnHeaderNormalizer.Normalize(document.OriginalFileName);
        var headers = document.Tables.SelectMany(x => x.Headers).Select(ColumnHeaderNormalizer.Normalize).ToArray();

        if (file.Contains("aereo") || file.Contains("air")
            || normalized.Contains("tarifarioairdivision")
            || headers.Any(x => x is "aerolinea" or "airline" or "100" or "300" or "500" or "1000"))
        {
            return TariffMode.Air;
        }

        if (file.Contains("ltl") || normalized.Contains("fleteterrestre")
            || headers.Any(x => x.Contains("ltlrate") || x.Contains("landfreightrate") || x == "wmrate"))
        {
            return TariffMode.Ltl;
        }

        if (file.Contains("lcl") || file.Contains("consolidaciones")
            || normalized.Contains("tarifariolcl")
            || normalized.Contains("cfscargue")
            || headers.Any(x => x.Contains("oceanfreightwm") || x == "ofrmin" || x.Contains("lclrate")))
        {
            return TariffMode.Lcl;
        }

        return TariffMode.Fcl;
    }

    private static (string? Origin, string? Destination) InferRoute(ExtractedDocument document, string context)
    {
        var routeText = $"{document.OriginalFileName}\n{document.RawText}\n{context}";
        var airport = Regex.Match(routeText, @"\b(?<from>[A-Z]{3})\s*[-–]\s*(?<to>[A-Z]{2,3})\b");
        if (airport.Success)
        {
            return (NormalizeRouteCode(airport.Groups["from"].Value), NormalizeRouteCode(airport.Groups["to"].Value));
        }

        var normalized = ColumnHeaderNormalizer.Normalize(routeText);
        if (normalized.Contains("zonalibredecolonciudaddeguatemala"))
        {
            return ("Zona Libre de Colón", "Ciudad de Guatemala");
        }
        if (normalized.Contains("miamisj") || normalized.Contains("miamisjo"))
        {
            return ("Miami", "SJO");
        }
        if (normalized.Contains("madritsjo") || normalized.Contains("madsjo"))
        {
            return ("MAD", "SJO");
        }

        return (null, null);
    }

    private static string? InferDirectionalDefault(
        ExtractedDocument document,
        ExtractedTable table,
        string context,
        bool wantOrigin
    )
    {
        var sheet = ColumnHeaderNormalizer.Normalize(table.SheetName);
        var normalized = ColumnHeaderNormalizer.Normalize(context);

        if (wantOrigin && sheet.StartsWith("export", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains("sanjosecostarica"))
        {
            return "San José, Costa Rica";
        }

        if (!wantOrigin && sheet.StartsWith("import", StringComparison.OrdinalIgnoreCase)
            && normalized.Contains("sanjosecostarica"))
        {
            return "San José, Costa Rica";
        }

        if (!wantOrigin && (normalized.Contains("haciaguatemala") || normalized.Contains("destinofinaldelacargaciudaddeguatemala")))
        {
            return "Ciudad de Guatemala";
        }

        return null;
    }

    private static string? InferTableDefaultOrigin(ExtractedTable table)
    {
        foreach (var row in table.Rows)
        {
            var cells = row.Values.Values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).ToArray();
            for (var i = 0; i < cells.Length - 1; i++)
            {
                var marker = ColumnHeaderNormalizer.Normalize(cells[i]);
                if (marker is "origin" or "origen")
                {
                    var candidate = cells[i + 1];
                    if (candidate.Length >= 3 && MoneyNormalizer.Normalize(candidate) is null)
                    {
                        return candidate;
                    }
                }
            }
        }

        return null;
    }

    private static string? InferNamedOrigin(string context)
    {
        var normalized = ColumnHeaderNormalizer.Normalize(context);
        if (normalized.Contains("zonalibredecolon")) return "Zona Libre de Colón";
        if (normalized.Contains("miami")) return "Miami";
        if (normalized.Contains("mad")) return "MAD";
        return null;
    }

    private static string? InferNamedDestination(string context)
    {
        var normalized = ColumnHeaderNormalizer.Normalize(context);
        if (normalized.Contains("ciudaddeguatemala") || normalized.Contains("haciaguatemala")) return "Ciudad de Guatemala";
        if (normalized.Contains("sanjose") || normalized.Contains("sjo")) return "SJO";
        return null;
    }

    private static string? InferAirOrigin(string line, string context)
    {
        var code = Regex.Match(line, @"^\s*(?<code>[A-Z]{3})\b");
        if (code.Success) return code.Groups["code"].Value;
        return InferNamedOrigin(context);
    }

    private static string? InferAirDestination(string context) => InferNamedDestination(context);

    private static string NormalizeRouteCode(string value) =>
        value.Equals("SJ", StringComparison.OrdinalIgnoreCase) ? "SJO" : value.ToUpperInvariant();

    private static decimal? ExtractWmRate(string context)
    {
        var match = Regex.Match(
            context,
            @"(?:US\$|USD\s*|\$)\s*(?<amount>\d+(?:[.,]\d+)?)\s*w\s*/?\s*m",
            RegexOptions.IgnoreCase
        );
        return match.Success ? MoneyNormalizer.Normalize(match.Value) : null;
    }

    private static string InferCurrency(
        IReadOnlyDictionary<string, string?> values,
        string context,
        TariffMode mode
    )
    {
        var rowText = string.Join(' ', values.Values.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (rowText.Contains('€') || Regex.IsMatch(rowText, @"\bEUR\b", RegexOptions.IgnoreCase)) return "EUR";
        if (Regex.IsMatch(rowText, @"\b(?:GBP)\b", RegexOptions.IgnoreCase) || rowText.Contains('£')) return "GBP";
        if (Regex.IsMatch(rowText, @"\b(?:CRC)\b", RegexOptions.IgnoreCase) || rowText.Contains('₡')) return "CRC";
        if (Regex.IsMatch(context, @"Currency\s*:?\s*EUR\b", RegexOptions.IgnoreCase) || (mode == TariffMode.Air && context.Contains('€'))) return "EUR";
        return "USD";
    }

    private static bool TryInferValidity(string text, out DateTime validFrom, out DateTime validTo)
    {
        validFrom = default;
        validTo = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var year = ResolveYear(text);

        var fullRange = Regex.Match(
            text,
            @"(?<d1>\d{1,2})\s*(?:de\s*)?(?<m1>enero|febrero|marzo|abril|mayo|junio|julio|agosto|septiembre|setiembre|octubre|noviembre|diciembre|january|february|march|april|may|june|july|august|september|october|november|december)\s*(?:de\s*)?(?<y1>20\d{2})?\s*(?:al|a|to|[-–])\s*(?<d2>\d{1,2})\s*(?:de\s*)?(?<m2>enero|febrero|marzo|abril|mayo|junio|julio|agosto|septiembre|setiembre|octubre|noviembre|diciembre|january|february|march|april|may|june|july|august|september|october|november|december)\s*(?:de\s*)?(?<y2>20\d{2})?",
            RegexOptions.IgnoreCase
        );
        if (fullRange.Success)
        {
            var y1 = ParseYear(fullRange.Groups["y1"].Value, year);
            var y2 = ParseYear(fullRange.Groups["y2"].Value, y1);
            if (TryBuildDate(fullRange.Groups["d1"].Value, fullRange.Groups["m1"].Value, y1, out validFrom)
                && TryBuildDate(fullRange.Groups["d2"].Value, fullRange.Groups["m2"].Value, y2, out validTo))
            {
                return validTo >= validFrom;
            }
        }

        var sameMonthRange = Regex.Match(
            text,
            @"(?<d1>\d{1,2})\s*(?:al|a|to|[-–])\s*(?<d2>\d{1,2})\s*(?:de\s*)?(?<month>enero|febrero|marzo|abril|mayo|junio|julio|agosto|septiembre|setiembre|octubre|noviembre|diciembre|january|february|march|april|may|june|july|august|september|october|november|december)\s*(?:de\s*)?(?<year>20\d{2})?",
            RegexOptions.IgnoreCase
        );
        if (sameMonthRange.Success)
        {
            var y = ParseYear(sameMonthRange.Groups["year"].Value, year);
            if (TryBuildDate(sameMonthRange.Groups["d1"].Value, sameMonthRange.Groups["month"].Value, y, out validFrom)
                && TryBuildDate(sameMonthRange.Groups["d2"].Value, sameMonthRange.Groups["month"].Value, y, out validTo))
            {
                return validTo >= validFrom;
            }
        }

        var numericRange = Regex.Match(
            text,
            @"(?<d1>\d{1,2})[/-](?<m1>\d{1,2})[/-](?<y1>20\d{2}).{0,30}?(?:al|a|to|[-–]).{0,10}?(?<d2>\d{1,2})[/-](?<m2>\d{1,2})[/-](?<y2>20\d{2})",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );
        if (numericRange.Success
            && TryBuildNumericDate(numericRange, "d1", "m1", "y1", out validFrom)
            && TryBuildNumericDate(numericRange, "d2", "m2", "y2", out validTo))
        {
            return validTo >= validFrom;
        }

        var fortnight = Regex.Match(
            text,
            @"(?:\b1Q\b|primera\s+quincena|1(?:era|ra)?\s+quincena).{0,20}?(?<month>enero|febrero|marzo|abril|mayo|junio|julio|agosto|septiembre|setiembre|octubre|noviembre|diciembre|january|february|march|april|may|june|july|august|september|october|november|december)|(?<month2>enero|febrero|marzo|abril|mayo|junio|julio|agosto|septiembre|setiembre|octubre|noviembre|diciembre|january|february|march|april|may|june|july|august|september|october|november|december).{0,15}?\b1Q\b",
            RegexOptions.IgnoreCase
        );
        if (fortnight.Success)
        {
            var monthText = fortnight.Groups["month"].Success ? fortnight.Groups["month"].Value : fortnight.Groups["month2"].Value;
            if (Months.TryGetValue(monthText, out var month))
            {
                validFrom = new DateTime(year, month, 1);
                validTo = new DateTime(year, month, 15);
                return true;
            }
        }

        var numericEnd = Regex.Match(text, @"(?:validez|validity|vigencia|valido\s+al|válido\s+al)\s*:?\s*(?<d>\d{1,2})[/-](?<m>\d{1,2})[/-](?<y>20\d{2})", RegexOptions.IgnoreCase);
        if (numericEnd.Success && TryBuildNumericDate(numericEnd, "d", "m", "y", out validTo))
        {
            var start = Regex.Match(text, @"(?:fecha|date)\s*:?\s*(?<d>\d{1,2})[/-](?<m>\d{1,2})[/-](?<y>20\d{2})", RegexOptions.IgnoreCase);
            validFrom = start.Success && TryBuildNumericDate(start, "d", "m", "y", out var explicitStart)
                ? explicitStart
                : new DateTime(validTo.Year, validTo.Month, 1);
            return validTo >= validFrom;
        }

        var textualEnd = Regex.Match(
            text,
            @"(?:validez|validity|vigencia|valido\s+al|válido\s+al)\s*:?\s*(?<d>\d{1,2})\s*(?:de\s*)?(?<month>enero|febrero|marzo|abril|mayo|junio|julio|agosto|septiembre|setiembre|octubre|noviembre|diciembre|january|february|march|april|may|june|july|august|september|october|november|december)\s*(?:de\s*)?(?<y>20\d{2})?",
            RegexOptions.IgnoreCase
        );
        if (textualEnd.Success)
        {
            var y = ParseYear(textualEnd.Groups["y"].Value, year);
            if (TryBuildDate(textualEnd.Groups["d"].Value, textualEnd.Groups["month"].Value, y, out validTo))
            {
                validFrom = new DateTime(validTo.Year, validTo.Month, 1);
                return true;
            }
        }

        return false;
    }

    private static bool TryBuildNumericDate(Match match, string dayGroup, string monthGroup, string yearGroup, out DateTime date)
    {
        date = default;
        return int.TryParse(match.Groups[dayGroup].Value, out var day)
            && int.TryParse(match.Groups[monthGroup].Value, out var month)
            && int.TryParse(match.Groups[yearGroup].Value, out var year)
            && TryCreateDate(year, month, day, out date);
    }

    private static bool TryBuildDate(string dayText, string monthText, int year, out DateTime date)
    {
        date = default;
        return int.TryParse(dayText, out var day)
            && Months.TryGetValue(monthText, out var month)
            && TryCreateDate(year, month, day, out date);
    }

    private static bool TryCreateDate(int year, int month, int day, out DateTime date)
    {
        date = default;
        if (year < 2000 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month)) return false;
        date = new DateTime(year, month, day);
        return true;
    }

    private static int ResolveYear(string text)
    {
        var explicitYear = Regex.Match(text, @"\b(?<year>20\d{2})\b");
        return explicitYear.Success && int.TryParse(explicitYear.Groups["year"].Value, out var year)
            ? year
            : DateTime.UtcNow.Year;
    }

    private static int ParseYear(string value, int fallback) => int.TryParse(value, out var year) ? year : fallback;

    private static string BuildContext(ExtractedDocument document)
    {
        var builder = new StringBuilder();
        builder.AppendLine(document.OriginalFileName);
        builder.AppendLine(document.RawText);
        builder.AppendLine(document.MetadataJson);

        var count = 0;
        foreach (var table in document.Tables)
        {
            builder.AppendLine(table.SheetName);
            builder.AppendLine(string.Join(" | ", table.Headers));
            foreach (var row in table.Rows)
            {
                foreach (var value in row.Values.Values)
                {
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    builder.Append(value).Append(" | ");
                    if (++count >= 1500) return builder.ToString();
                }
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }

    private static string? Read(IReadOnlyDictionary<string, string?> values, params string[] aliases)
    {
        foreach (var alias in aliases)
        {
            var value = ReadByNormalizedKey(values, ColumnHeaderNormalizer.Normalize(alias));
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        return null;
    }

    private static string? ReadByNormalizedKey(IReadOnlyDictionary<string, string?> values, string normalizedAlias)
    {
        foreach (var pair in values)
        {
            if (ColumnHeaderNormalizer.Normalize(pair.Key) == normalizedAlias && !string.IsNullOrWhiteSpace(pair.Value))
            {
                return pair.Value!.Trim();
            }
        }
        return null;
    }

    private static void SetIfMissing(IDictionary<string, string?> values, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (Read((IReadOnlyDictionary<string, string?>)values, key) is null) values[key] = value.Trim();
    }

    private static string? FirstText(params string?[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();
}
