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
    private static readonly char[] Delimiters = ['|', '\t', ';', ','];

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
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        var tables = TryParseDelimitedTables(lines);

        if (tables.Count == 0)
        {
            var keyValueTable = TryBuildKeyValueTable(plainText);
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
            || text.Contains("Content-Type:", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeEmailBody(string text)
    {
        var decoded = WebUtility.HtmlDecode(text);

        if (decoded.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || decoded.Contains("<table", StringComparison.OrdinalIgnoreCase)
            || decoded.Contains("<td", StringComparison.OrdinalIgnoreCase)
            || decoded.Contains("<tr", StringComparison.OrdinalIgnoreCase))
        {
            decoded = Regex.Replace(decoded, @"<\s*br\s*/?>", "\n", RegexOptions.IgnoreCase);
            decoded = Regex.Replace(decoded, @"<\s*/?\s*tr[^>]*>", "\n", RegexOptions.IgnoreCase);
            decoded = Regex.Replace(decoded, @"<\s*/?\s*t[dh][^>]*>", "|", RegexOptions.IgnoreCase);
            decoded = Regex.Replace(decoded, @"<[^>]+>", " ", RegexOptions.IgnoreCase);
        }

        return decoded;
    }

    private static List<ExtractedTable> TryParseDelimitedTables(IReadOnlyCollection<string> lines)
    {
        var lineArray = lines.ToArray();
        var tables = new List<ExtractedTable>();

        for (var i = 0; i < lineArray.Length; i++)
        {
            var headerFields = SplitLine(lineArray[i]);
            if (headerFields.Length < 2 || ScoreHeaders(headerFields) < 2)
            {
                continue;
            }

            var headers = NormalizeHeaders(headerFields);
            var rows = new List<ExtractedRow>();

            for (var rowIndex = i + 1; rowIndex < lineArray.Length; rowIndex++)
            {
                var fields = SplitLine(lineArray[rowIndex]);
                if (fields.Length < 2)
                {
                    continue;
                }

                if (ScoreHeaders(fields) >= 2 && rows.Count > 0)
                {
                    break;
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

    private static ExtractedTable? TryBuildKeyValueTable(string text)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["POL"] = FindValue(text, "POL", "Origen", "Origin", "Port of Loading"),
            ["POD"] = FindValue(text, "POD", "Destino", "Destination", "Port of Discharge"),
            ["Carrier"] = FindValue(text, "Carrier", "Naviera", "Shipping line", "Línea naviera"),
            ["Container"] = FindValue(text, "Container", "Equipment", "Equipo", "Tipo de contenedor"),
            ["Currency"] = FindValue(text, "Currency", "Moneda", "CCY") ?? InferCurrency(text),
            ["Free Days"] = FindValue(text, "Free Days", "Free time", "Días libres", "Dias libres"),
            ["Valid From"] = FindValue(text, "Valid From", "Vigencia desde", "Desde", "Effective From"),
            ["Valid To"] = FindValue(text, "Valid To", "Valid Until", "Vigencia hasta", "Hasta", "Expiration"),
            ["Ocean Freight"] = FindValue(text, "Ocean Freight", "Freight", "Flete", "Tarifa", "Rate", "Precio"),
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
            var pattern = $@"(?:^|[\r\n\|;])\s*{Regex.Escape(label)}\s*[:\-]?\s*(?<value>[^\r\n\|;]+)";
            var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (match.Success)
            {
                var value = match.Groups["value"].Value.Trim();
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

    private static string[] SplitLine(string line)
    {
        foreach (var delimiter in Delimiters)
        {
            if (line.Count(ch => ch == delimiter) >= 1)
            {
                return line
                    .Split(delimiter, StringSplitOptions.TrimEntries)
                    .Select(x => x.Trim())
                    .ToArray();
            }
        }

        return Regex.Split(line.Trim(), @"\s{2,}")
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
    }

    private static int ScoreHeaders(IReadOnlyCollection<string> headers)
    {
        return headers.Count(header =>
        {
            var normalized = ColumnHeaderNormalizer.Normalize(header);
            return DefaultFclColumnMappings.Mappings.ContainsKey(normalized)
                || Regex.IsMatch(normalized, @"^(20|40|45)(gp|dc|dv|hc|hq|ft|std|dry)?(usd|rate|flete|tarifa|amount|precio)?$", RegexOptions.IgnoreCase);
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
        return Regex.Replace(value, @"\s+", " ").Trim();
    }
}
