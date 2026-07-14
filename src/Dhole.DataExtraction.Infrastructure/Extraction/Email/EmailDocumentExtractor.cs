using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using Dhole.DataExtraction.Infrastructure.Mapping;

namespace Dhole.DataExtraction.Infrastructure.Extraction.Email;

public sealed class EmailDocumentExtractor : IDocumentExtractor
{
    public SourceFileType FileType => SourceFileType.Email;

    public Task<ExtractedDocument> ExtractAsync(
        DocumentExtractionInput input,
        CancellationToken cancellationToken = default
    )
    {
        var text = DecodeText(input.FileContent);
        var body = ExtractBody(text);
        var plainText = NormalizeEmailBody(body);

        var lines = plainText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeLine)
            .Select(x => x.Trim().TrimStart('>', '|', '-', '*').Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        var tables = TryParseDelimitedTables(lines);

        if (tables.Count == 0)
        {
            tables.AddRange(TryBuildMultiRowKeyValueTables(lines));
        }

        if (tables.Count == 0)
        {
            var keyValueTable = TryBuildSingleKeyValueTable(plainText);
            if (keyValueTable is not null)
            {
                tables.Add(keyValueTable);
            }
        }

        if (tables.Count == 0)
        {
            tables.Add(new ExtractedTable("EMAIL", [], []));
        }

        return Task.FromResult(
            new ExtractedDocument(input.OriginalFileName, SourceFileType.Email, tables, plainText)
        );
    }

    private static string DecodeText(byte[] content)
    {
        try
        {
            return Encoding.UTF8.GetString(content);
        }
        catch
        {
            return Encoding.Latin1.GetString(content);
        }
    }

    private static string ExtractBody(string text)
    {
        var normalized = text.Replace("=\r\n", string.Empty).Replace("=\n", string.Empty);
        var headerEnd = normalized.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (headerEnd >= 0 && LooksLikeEmailHeaders(normalized[..headerEnd]))
        {
            return normalized[(headerEnd + 4)..];
        }

        headerEnd = normalized.IndexOf("\n\n", StringComparison.Ordinal);
        if (headerEnd >= 0 && LooksLikeEmailHeaders(normalized[..headerEnd]))
        {
            return normalized[(headerEnd + 2)..];
        }

        return normalized;
    }

    private static bool LooksLikeEmailHeaders(string text)
    {
        return text.Contains("From:", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Subject:", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Content-Type:", StringComparison.OrdinalIgnoreCase)
            || text.Contains("MIME-Version:", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeEmailBody(string text)
    {
        var decoded = WebUtility.HtmlDecode(text)
            .Replace("\u00A0", " ", StringComparison.Ordinal)
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase);

        decoded = Regex.Replace(decoded, @"<\s*style[^>]*>.*?<\s*/\s*style\s*>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        decoded = Regex.Replace(decoded, @"<\s*script[^>]*>.*?<\s*/\s*script\s*>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        decoded = Regex.Replace(decoded, @"<\s*br\s*/?>", "\n", RegexOptions.IgnoreCase);
        decoded = Regex.Replace(decoded, @"<\s*/\s*(div|p|li|h[1-6])\s*>", "\n", RegexOptions.IgnoreCase);
        decoded = Regex.Replace(decoded, @"<\s*(div|p|li|h[1-6])[^>]*>", "\n", RegexOptions.IgnoreCase);
        decoded = Regex.Replace(decoded, @"<\s*/?\s*tr[^>]*>", "\n", RegexOptions.IgnoreCase);
        decoded = Regex.Replace(decoded, @"<\s*/?\s*t[dh][^>]*>", "|", RegexOptions.IgnoreCase);
        decoded = Regex.Replace(decoded, @"<[^>]+>", " ", RegexOptions.IgnoreCase);
        // Preserve tabs and repeated spaces because plain-text email clients use them
        // as table column separators. Collapsing them makes valid FCL tables unreadable.
        decoded = Regex.Replace(decoded, @"[ \t]+(?=\r?$)", string.Empty, RegexOptions.Multiline);
        decoded = Regex.Replace(decoded, @"\n{2,}", "\n");

        return decoded.Trim();
    }

    private static List<ExtractedTable> TryParseDelimitedTables(IReadOnlyCollection<string> lines)
    {
        var lineArray = lines.ToArray();
        var tables = new List<ExtractedTable>();

        for (var i = 0; i < lineArray.Length; i++)
        {
            var headerSplit = TrySplitHeaderLine(lineArray[i]);
            if (headerSplit is null)
            {
                continue;
            }

            var headers = NormalizeHeaders(headerSplit.Fields);
            var rows = new List<ExtractedRow>();

            for (var rowIndex = i + 1; rowIndex < lineArray.Length; rowIndex++)
            {
                var fields = SplitLine(lineArray[rowIndex], headerSplit.Mode);
                if (fields.Length < 2)
                {
                    if (rows.Count > 0)
                    {
                        break;
                    }

                    continue;
                }

                if (ScoreHeaders(fields) >= 2)
                {
                    if (rows.Count > 0)
                    {
                        break;
                    }

                    continue;
                }

                if (fields.Length < Math.Min(headers.Length, 3))
                {
                    if (rows.Count > 0)
                    {
                        break;
                    }

                    continue;
                }

                var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                for (var columnIndex = 0; columnIndex < headers.Length; columnIndex++)
                {
                    values[headers[columnIndex]] = columnIndex < fields.Length && !string.IsNullOrWhiteSpace(fields[columnIndex])
                        ? fields[columnIndex].Trim()
                        : null;
                }

                if (values.Values.Any(x => !string.IsNullOrWhiteSpace(x)))
                {
                    rows.Add(new ExtractedRow(rowIndex + 1, values, JsonSerializer.Serialize(values)));
                }
            }

            if (rows.Count > 0)
            {
                tables.Add(new ExtractedTable("EMAIL", headers, rows));
                break;
            }
        }

        return tables;
    }

    private static HeaderSplit? TrySplitHeaderLine(string line)
    {
        var candidates = new List<HeaderSplit>();

        AddDelimitedCandidate('|', LineSplitMode.Pipe, minimumOccurrences: 1);
        AddDelimitedCandidate('\t', LineSplitMode.Tab, minimumOccurrences: 1);
        AddDelimitedCandidate(';', LineSplitMode.Semicolon, minimumOccurrences: 1);
        AddDelimitedCandidate(',', LineSplitMode.Comma, minimumOccurrences: 2);

        var whitespaceFields = SplitLine(line, LineSplitMode.AlignedWhitespace);
        if (whitespaceFields.Length >= 2 && ScoreHeaders(whitespaceFields) >= 2)
        {
            candidates.Add(new HeaderSplit(whitespaceFields, LineSplitMode.AlignedWhitespace));
        }

        return candidates
            .OrderByDescending(candidate => ScoreHeaders(candidate.Fields))
            .ThenByDescending(candidate => candidate.Fields.Length)
            .FirstOrDefault();

        void AddDelimitedCandidate(char delimiter, LineSplitMode mode, int minimumOccurrences)
        {
            if (line.Count(character => character == delimiter) < minimumOccurrences)
            {
                return;
            }

            var fields = SplitLine(line, mode);
            if (fields.Length >= 2 && ScoreHeaders(fields) >= 2)
            {
                candidates.Add(new HeaderSplit(fields, mode));
            }
        }
    }

    private static IReadOnlyCollection<ExtractedTable> TryBuildMultiRowKeyValueTables(IReadOnlyCollection<string> lines)
    {
        var rows = new List<Dictionary<string, string?>>();
        var current = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var pairs = ExtractKeyValuePairs(line).ToArray();
            if (pairs.Length == 0)
            {
                continue;
            }

            foreach (var pair in pairs)
            {
                var canonicalKey = NormalizeEmailKey(pair.Key);

                if (canonicalKey is null || string.IsNullOrWhiteSpace(pair.Value))
                {
                    continue;
                }

                if (ShouldStartNewRow(current, canonicalKey))
                {
                    AddCurrentRow(rows, current);
                    current = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                }

                current[canonicalKey] = CleanValue(pair.Value);
            }
        }

        AddCurrentRow(rows, current);

        if (rows.Count == 0)
        {
            return [];
        }

        var headers = new[]
        {
            "Carrier",
            "Agent",
            "POL",
            "POE",
            "POD",
            "ContainerSize",
            "Commodity",
            "Currency",
            "FreightAmount",
            "FixedCosts",
            "ValidFrom",
            "ValidTo",
            "TransitTimeDays",
            "FreeDays",
            "Remarks",
        };

        var extractedRows = rows
            .Select((row, index) =>
            {
                var values = headers.ToDictionary(
                    header => header,
                    header => row.TryGetValue(header, out var value) && !string.IsNullOrWhiteSpace(value)
                        ? value
                        : null,
                    StringComparer.OrdinalIgnoreCase
                );

                return new ExtractedRow(index + 1, values, JsonSerializer.Serialize(values));
            })
            .ToArray();

        return [new ExtractedTable("EMAIL", headers, extractedRows)];
    }

    private static IEnumerable<(string Key, string Value)> ExtractKeyValuePairs(string line)
    {
        var normalizedLine = line.Trim().Trim('|', ';', ',').Trim();
        if (string.IsNullOrWhiteSpace(normalizedLine))
        {
            yield break;
        }

        var matches = Regex.Matches(
            normalizedLine,
            @"(?<key>[A-Za-zÁÉÍÓÚÜÑáéíóúüñ][A-Za-zÁÉÍÓÚÜÑáéíóúüñ0-9\s/_().-]{0,60})\s*[:=]\s*(?<value>.*?)(?=\s+[A-Za-zÁÉÍÓÚÜÑáéíóúüñ][A-Za-zÁÉÍÓÚÜÑáéíóúüñ0-9\s/_().-]{0,60}\s*[:=]|$)",
            RegexOptions.IgnoreCase
        );

        foreach (Match match in matches)
        {
            var key = match.Groups["key"].Value.Trim().Trim('|', ';', ',');
            var value = match.Groups["value"].Value.Trim().Trim('|', ';', ',');

            if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
            {
                yield return (key, value);
            }
        }
    }

    private static bool ShouldStartNewRow(IReadOnlyDictionary<string, string?> current, string canonicalKey)
    {
        if (current.Count == 0)
        {
            return false;
        }

        if (!current.ContainsKey(canonicalKey))
        {
            return false;
        }

        return canonicalKey is "Carrier" or "POL" or "POD" or "FreightAmount";
    }

    private static void AddCurrentRow(
        List<Dictionary<string, string?>> rows,
        Dictionary<string, string?> current
    )
    {
        if (current.Count == 0)
        {
            return;
        }

        var usefulValues = current.Values.Count(x => !string.IsNullOrWhiteSpace(x));
        var hasRoute = current.ContainsKey("POL") || current.ContainsKey("POD");
        var hasAmount = current.ContainsKey("FreightAmount");
        var hasCarrier = current.ContainsKey("Carrier");

        if (usefulValues >= 4 && (hasRoute || hasAmount || hasCarrier))
        {
            rows.Add(new Dictionary<string, string?>(current, StringComparer.OrdinalIgnoreCase));
        }
    }

    private static string? NormalizeEmailKey(string value)
    {
        var normalized = ColumnHeaderNormalizer.Normalize(value);

        return normalized switch
        {
            "carrier" or "naviera" or "shippingline" or "lineamaritima" or "line" => "Carrier",
            "agent" or "agente" or "forwarder" or "provider" or "proveedor" => "Agent",
            "pol" or "origin" or "origen" or "originport" or "portofloading" or "loadingport" => "POL",
            "poe" or "portofexit" or "puertosalida" or "transshipmentport" or "via" => "POE",
            "pod" or "destination" or "destino" or "destinationport" or "portofdischarge" or "delivery" => "POD",
            "containersize" or "container" or "containertype" or "equipment" or "equipo" or "tipocontenedor" or "contenedor" => "ContainerSize",
            "commodity" or "mercancia" or "producto" or "cargo" => "Commodity",
            "currency" or "moneda" or "ccy" or "curr" => "Currency",
            "freightamount" or "freight" or "flete" or "oceanfreight" or "rate" or "tarifa" or "precio" or "amount" => "FreightAmount",
            "fixedcosts" or "fixedcost" or "costosfijos" or "costofijo" or "localcharges" or "charges" or "surcharges" => "FixedCosts",
            "validfrom" or "vigencia" or "vigenciadesde" or "inicio" or "fechainicio" or "start" or "startdate" or "desde" or "effectivefrom" or "effectivedate" => "ValidFrom",
            "validto" or "validuntil" or "vigenciahasta" or "vence" or "vencimiento" or "fechavencimiento" or "fin" or "fechafin" or "hasta" or "expiration" or "expirationdate" or "expiracion" or "validity" => "ValidTo",
            "transittimedays" or "transitdays" or "transittime" or "diastransito" or "tiempotransito" => "TransitTimeDays",
            "freedays" or "freetime" or "diaslibres" => "FreeDays",
            "remarks" or "observaciones" or "comentarios" or "comments" or "notes" => "Remarks",
            _ => null,
        };
    }

    private static ExtractedTable? TryBuildSingleKeyValueTable(string text)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["Carrier"] = FindValue(text, "Carrier", "Naviera", "Shipping line", "Línea naviera"),
            ["Agent"] = FindValue(text, "Agent", "Agente", "Provider", "Proveedor"),
            ["POL"] = FindValue(text, "POL", "Origen", "Origin", "Port of Loading"),
            ["POE"] = FindValue(text, "POE", "Port of Exit", "Puerto salida", "Via"),
            ["POD"] = FindValue(text, "POD", "Destino", "Destination", "Port of Discharge"),
            ["ContainerSize"] = FindValue(text, "ContainerSize", "Container Size", "Container", "Equipment", "Equipo", "Tipo de contenedor"),
            ["Commodity"] = FindValue(text, "Commodity", "Mercancía", "Mercancia", "Producto"),
            ["Currency"] = FindValue(text, "Currency", "Moneda", "CCY") ?? InferCurrency(text),
            ["FreightAmount"] = FindValue(text, "FreightAmount", "Freight Amount", "Ocean Freight", "Freight", "Flete", "Tarifa", "Rate", "Precio"),
            ["FixedCosts"] = FindValue(text, "FixedCosts", "Fixed Costs", "Costos fijos", "Local Charges", "Surcharges", "Charges"),
            ["ValidFrom"] = FindValue(text, "Valid From", "Vigencia", "Vigencia desde", "Inicio", "Fecha inicio", "Desde", "Effective From"),
            ["ValidTo"] = FindValue(text, "Valid To", "Valid Until", "Vigencia hasta", "Vence", "Vencimiento", "Expiración", "Expiracion", "Hasta", "Expiration", "Validity"),
            ["TransitTimeDays"] = FindValue(text, "TransitTimeDays", "Transit Time Days", "Transit Days", "Días tránsito", "Dias transito"),
            ["FreeDays"] = FindValue(text, "Free Days", "Free time", "Días libres", "Dias libres"),
            ["Remarks"] = FindValue(text, "Remarks", "Observaciones", "Comentarios", "Notes"),
        };

        var usefulValues = values.Values.Count(x => !string.IsNullOrWhiteSpace(x));
        if (usefulValues < 4)
        {
            return null;
        }

        var headers = values.Keys.ToArray();
        var row = new ExtractedRow(1, values, JsonSerializer.Serialize(values));
        return new ExtractedTable("EMAIL", headers, [row]);
    }

    private static string? FindValue(string text, params string[] labels)
    {
        foreach (var label in labels)
        {
            var pattern = $@"(?:^|[\r\n\|;])\s*{Regex.Escape(label)}\s*[:=\-]?\s*(?<value>[^\r\n\|;]+)";
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (match.Success)
            {
                var value = CleanValue(match.Groups["value"].Value);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }
        }

        return null;
    }

    private static string? InferCurrency(string text)
    {
        var match = Regex.Match(text, @"\b(USD|EUR|CRC)\b", RegexOptions.IgnoreCase);
        return match.Success ? match.Value.ToUpperInvariant() : null;
    }

    private static string[] SplitLine(string line, LineSplitMode mode)
    {
        var cleanLine = line.Trim().Trim('|').Trim();

        return mode switch
        {
            LineSplitMode.Pipe => SplitByDelimiter(cleanLine, '|'),
            LineSplitMode.Tab => SplitByDelimiter(cleanLine, '\t'),
            LineSplitMode.Semicolon => SplitByDelimiter(cleanLine, ';'),
            LineSplitMode.Comma => SplitByDelimiter(cleanLine, ','),
            _ => Regex.Split(cleanLine, @"\s{2,}")
                .Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray(),
        };
    }

    private static string[] SplitByDelimiter(string line, char delimiter)
    {
        return line
            .Split(delimiter, StringSplitOptions.TrimEntries)
            .Select(x => x.Trim().Trim('|'))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
    }

    private static int ScoreHeaders(IReadOnlyCollection<string> headers)
    {
        return headers.Count(header =>
        {
            var normalized = ColumnHeaderNormalizer.Normalize(header);
            return DefaultFclColumnMappings.Mappings.ContainsKey(normalized)
                || Regex.IsMatch(normalized, @"^(20|40|45)(gp|dc|dv|hc|hq|ft|std|dry)?(usd|rate|flete|tarifa|amount|precio|sale|venta)?$", RegexOptions.IgnoreCase);
        });
    }

    private static string[] NormalizeHeaders(IEnumerable<string> rawHeaders)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        return rawHeaders.Select(header =>
        {
            var value = string.IsNullOrWhiteSpace(header) ? "Column" : header.Trim();
            if (!seen.TryAdd(value, 1))
            {
                seen[value]++;
                value = $"{value} {seen[value]}";
            }

            return value;
        }).ToArray();
    }

    private static string NormalizeLine(string value)
    {
        return value
            .Replace("¦", "|", StringComparison.Ordinal)
            .Replace("│", "|", StringComparison.Ordinal)
            .Replace("┃", "|", StringComparison.Ordinal)
            .Replace("\u00A0", " ", StringComparison.Ordinal)
            .Trim();
    }

    private static string CleanValue(string value)
    {
        return value
            .Trim()
            .Trim('|', ';', ',')
            .Replace("\u00A0", " ", StringComparison.Ordinal)
            .Trim();
    }

    private enum LineSplitMode
    {
        Pipe,
        Tab,
        Semicolon,
        Comma,
        AlignedWhitespace,
    }

    private sealed record HeaderSplit(string[] Fields, LineSplitMode Mode);
}
