using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Dhole.DataExtraction.Application.Abstractions.Extraction;

namespace Dhole.DataExtraction.Infrastructure.Extraction.Email;

/// <summary>
/// Parses HTML email tables before they are flattened into text. Rowspan keeps shared
/// route/carrier/validity cells associated with every physical rate row. The output
/// remains the same ExtractedTable model consumed by the existing mapping/validation
/// pipeline; it never creates rates or guesses missing values.
/// </summary>
public static class HtmlTariffTableReader
{
    private const int MaximumHtmlCharacters = 2_000_000;
    private const int MaximumTables = 32;
    private const int MaximumRowsPerTable = 5_000;
    private const int MaximumColumns = 64;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private static readonly Regex TableTags = new(
        @"<table\b[^>]*>[\s\S]*?</table\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout
    );
    private static readonly Regex RowTags = new(
        @"<tr\b[^>]*>(?<content>[\s\S]*?)</tr\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout
    );
    private static readonly Regex CellTags = new(
        @"<(?<tag>td|th)\b(?<attributes>[^>]*)>(?<content>[\s\S]*?)</(?:td|th)\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout
    );
    private static readonly Regex HtmlTags = new(
        @"<[^>]*>",
        RegexOptions.Singleline | RegexOptions.Compiled,
        RegexTimeout
    );
    private static readonly Regex UnsafeBlocks = new(
        @"<(?:script|style|svg)\b[^>]*>[\s\S]*?</(?:script|style|svg)\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout
    );

    public static IReadOnlyCollection<ExtractedTable> Extract(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)
            || html.Length > MaximumHtmlCharacters
            || !html.Contains("<table", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        try
        {
            var result = new List<ExtractedTable>();
            var tableNumber = 0;
            foreach (Match tableMatch in TableTags.Matches(html))
            {
                if (++tableNumber > MaximumTables)
                    break;

                var rows = ParseRows(tableMatch.Value);
                if (rows.Count < 2)
                    continue;

                var headerIndex = rows.FindIndex(row => row.Header && row.Cells.Count >= 2);
                if (headerIndex < 0)
                    headerIndex = 0;

                var headers = BuildHeaders(rows[headerIndex].Cells);
                if (headers.Length < 2)
                    continue;

                var extractedRows = new List<ExtractedRow>();
                for (var index = headerIndex + 1; index < rows.Count; index++)
                {
                    var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
                    for (var column = 0; column < headers.Length; column++)
                    {
                        rows[index].Cells.TryGetValue(column, out var value);
                        values[headers[column]] = string.IsNullOrWhiteSpace(value) ? null : value;
                    }

                    if (values.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
                        extractedRows.Add(new ExtractedRow(rows[index].Number, values));
                }

                // Do not treat layout/signature tables as tariff tables. Other
                // formats and unrecognized tables still have the existing fallback.
                if (extractedRows.Count == 0 || !LooksLikeRateTable(headers, extractedRows))
                    continue;

                result.Add(new ExtractedTable($"HTML table {tableNumber}", headers, extractedRows));
            }

            return result;
        }
        catch (RegexMatchTimeoutException)
        {
            // Malformed or adversarial HTML must not disable the ordinary text
            // extractor / AI fallback for this email.
            return [];
        }
    }

    /// <summary>
    /// Lossless-for-extracted-cells, HTML-encoded rendering for AI evidence.
    /// Only a fixed element vocabulary is emitted; original markup is never echoed.
    /// </summary>
    public static string ToCanonicalHtml(IEnumerable<ExtractedTable> tables)
    {
        var builder = new StringBuilder("<section data-source=\"email-tables\">");
        foreach (var table in tables)
        {
            builder.Append("<table><caption>")
                .Append(WebUtility.HtmlEncode(table.SheetName))
                .Append("</caption><thead><tr>");
            foreach (var header in table.Headers)
                builder.Append("<th>").Append(WebUtility.HtmlEncode(header)).Append("</th>");
            builder.Append("</tr></thead><tbody>");

            foreach (var row in table.Rows)
            {
                builder.Append("<tr>");
                foreach (var header in table.Headers)
                {
                    row.Values.TryGetValue(header, out var value);
                    builder.Append("<td>")
                        .Append(WebUtility.HtmlEncode(value ?? string.Empty))
                        .Append("</td>");
                }
                builder.Append("</tr>");
            }
            builder.Append("</tbody></table>");
        }
        return builder.Append("</section>").ToString();
    }

    private static List<HtmlRow> ParseRows(string html)
    {
        var rows = new List<HtmlRow>();
        var spanningCells = new Dictionary<int, SpanValue>();
        var number = 0;

        foreach (Match rowMatch in RowTags.Matches(html))
        {
            if (++number > MaximumRowsPerTable)
                break;

            var cells = new Dictionary<int, string>();
            var column = 0;
            var header = false;

            // Apply a spanning cell only once per physical row, never once per
            // subsequent table cell. Colspan occupies positions but does not
            // duplicate money amounts in neighboring container columns.
            void FillSpanning(int until)
            {
                while (column < until && column < MaximumColumns)
                {
                    if (spanningCells.TryGetValue(column, out var pending))
                    {
                        cells[column] = pending.Value;
                        if (pending.Remaining <= 1)
                            spanningCells.Remove(column);
                        else
                            spanningCells[column] = pending with { Remaining = pending.Remaining - 1 };
                    }
                    column++;
                }
            }

            foreach (Match cell in CellTags.Matches(rowMatch.Groups["content"].Value))
            {
                if (column >= MaximumColumns)
                    break;

                while (column < MaximumColumns && spanningCells.ContainsKey(column))
                    FillSpanning(column + 1);

                if (column >= MaximumColumns)
                    break;

                header |= cell.Groups["tag"].Value.Equals("th", StringComparison.OrdinalIgnoreCase);
                var attributes = cell.Groups["attributes"].Value;
                var colspan = ReadSpan(attributes, "colspan");
                var rowspan = ReadSpan(attributes, "rowspan");
                var value = CleanCell(cell.Groups["content"].Value);

                for (var offset = 0; offset < colspan && column + offset < MaximumColumns; offset++)
                {
                    var target = column + offset;
                    var part = offset == 0 ? value : string.Empty;
                    cells[target] = part;
                    if (rowspan > 1)
                        spanningCells[target] = new SpanValue(part, rowspan - 1);
                }
                column += colspan;
            }

            // A row can finish with merged cells and no explicit <td> at those
            // coordinates; materialize the remaining active spans.
            foreach (var index in spanningCells.Keys.Where(index => index >= column).Order().ToArray())
            {
                if (index >= MaximumColumns)
                    break;
                var pending = spanningCells[index];
                cells[index] = pending.Value;
                if (pending.Remaining <= 1)
                    spanningCells.Remove(index);
                else
                    spanningCells[index] = pending with { Remaining = pending.Remaining - 1 };
            }

            if (cells.Count > 0)
                rows.Add(new HtmlRow(number, header, cells));
        }
        return rows;
    }

    private static string[] BuildHeaders(IReadOnlyDictionary<int, string> cells)
    {
        var result = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var width = Math.Min(MaximumColumns, cells.Keys.DefaultIfEmpty(-1).Max() + 1);

        for (var index = 0; index < width; index++)
        {
            cells.TryGetValue(index, out var header);
            var baseName = string.IsNullOrWhiteSpace(header) ? $"Column{index + 1}" : header.Trim();
            var name = baseName;
            for (var suffix = 2; !used.Add(name); suffix++)
                name = $"{baseName}_{suffix}";
            result.Add(name);
        }
        return result.ToArray();
    }

    private static bool LooksLikeRateTable(
        IReadOnlyCollection<string> headers,
        IReadOnlyCollection<ExtractedRow> rows
    )
    {
        var headerText = string.Join(" ", headers);
        var hasCommercialHeaders = Regex.IsMatch(
            headerText,
            @"\b(POL|POD|POE|carrier|naviera|origin|destination|flete|freight|rate|tarifa|precio|amount|free.?time|vigencia)\b|(?:20|40|45)\s*['’]?",
            RegexOptions.IgnoreCase,
            RegexTimeout
        );
        var hasAmount = rows.Any(row => row.Values.Values.Any(value =>
            !string.IsNullOrWhiteSpace(value)
            && Regex.IsMatch(value, @"(?:[$€₡]\s*)?\d[\d,.\s]*", RegexOptions.None, RegexTimeout)));

        return hasCommercialHeaders && hasAmount;
    }

    private static int ReadSpan(string attributes, string name)
    {
        var match = Regex.Match(
            attributes,
            $@"\b{name}\s*=\s*['""]?(?<count>\d+)",
            RegexOptions.IgnoreCase,
            RegexTimeout
        );
        return match.Success && int.TryParse(match.Groups["count"].Value, out var count)
            ? Math.Clamp(count, 1, MaximumColumns)
            : 1;
    }

    private static string CleanCell(string html)
    {
        var text = UnsafeBlocks.Replace(html, string.Empty);
        // Keep image alt text for carrier logos when supplied by the sender.
        text = Regex.Replace(text, @"<img\b[^>]*\balt\s*=\s*[""'](?<alt>[^""']*)[""'][^>]*>",
            match => match.Groups["alt"].Value, RegexOptions.IgnoreCase, RegexTimeout);
        text = Regex.Replace(text, @"<br\s*/?>", " ", RegexOptions.IgnoreCase, RegexTimeout);
        text = HtmlTags.Replace(text, " ");
        text = WebUtility.HtmlDecode(text).Replace('\u00a0', ' ');
        text = Regex.Replace(text, @"\s+", " ", RegexOptions.None, RegexTimeout).Trim();
        return text.Length > 2_000 ? text[..2_000] : text;
    }

    private sealed record HtmlRow(int Number, bool Header, IReadOnlyDictionary<int, string> Cells);
    private sealed record SpanValue(string Value, int Remaining);
}
