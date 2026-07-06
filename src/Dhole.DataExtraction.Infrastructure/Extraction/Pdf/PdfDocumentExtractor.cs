using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dhole.DataExtraction.Infrastructure.Mapping;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Dhole.DataExtraction.Infrastructure.Extraction.Pdf;

public sealed class PdfDocumentExtractor : IDocumentExtractor
{
    private const double RowTolerance = 3.0d;

    public SourceFileType FileType => SourceFileType.Pdf;

    public Task<ExtractedDocument> ExtractAsync(
        DocumentExtractionInput input,
        CancellationToken cancellationToken = default
    )
    {
        using var stream = new MemoryStream(input.FileContent);
        using var document = PdfDocument.Open(stream);

        var lines = new List<string>();

        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();

            // PdfPig's Page.Text is not reliable for tables because it can collapse an entire
            // page into a single line. The word-position reconstruction keeps the visual rows.
            lines.AddRange(ExtractLinesFromWords(page));

            if (!string.IsNullOrWhiteSpace(page.Text))
            {
                lines.AddRange(
                    page.Text.Split(
                        ['\r', '\n'],
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                    )
                );
            }
        }

        // Do not Distinct() here. Some PDFs expose tables as one cell per line and repeat
        // values such as POD/carrier on every row. Removing repeated cells breaks the
        // visual-cell parser and makes valid PDFs look empty/invalid.
        var normalizedLines = lines
            .Select(NormalizeLine)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        var rawText = string.Join(Environment.NewLine, normalizedLines);

        // Some recurring carrier PDFs expose the table as a visual cell stream:
        // Header cells first, then one value per line. In that shape, generic whitespace
        // parsing can produce a wrong partial row and later validation rejects the file.
        // Prefer the FCL cell-stream parser when it can rebuild complete pricing rows.
        var tables = TryParseFclCellStreamTables(normalizedLines);

        if (tables.Count == 0)
        {
            tables = TryParsePipeDelimitedTables(normalizedLines);
        }

        if (tables.Count == 0)
        {
            tables = TryParseWhitespaceDelimitedTables(normalizedLines);
        }

        if (!tables.Any(table => table.Rows.Count > 0))
        {
            var cellStreamTables = TryParseFclCellStreamTables(normalizedLines);

            if (cellStreamTables.Count > 0)
            {
                tables = cellStreamTables;
            }
        }

        if (tables.Count == 0)
        {
            tables.Add(new ExtractedTable("PDF", [], []));
        }

        return Task.FromResult(
            new ExtractedDocument(input.OriginalFileName, SourceFileType.Pdf, tables, rawText)
        );
    }

    private static IReadOnlyCollection<string> ExtractLinesFromWords(Page page)
    {
        var words = page.GetWords()
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .OrderByDescending(word => word.BoundingBox.Bottom)
            .ThenBy(word => word.BoundingBox.Left)
            .ToArray();

        var result = new List<string>();
        var currentRow = new List<Word>();
        double? currentY = null;

        foreach (var word in words)
        {
            var wordY = word.BoundingBox.Bottom;

            if (currentY is null || Math.Abs(currentY.Value - wordY) <= RowTolerance)
            {
                currentRow.Add(word);
                currentY ??= wordY;
                continue;
            }

            AddCurrentRow();
            currentRow = [word];
            currentY = wordY;
        }

        AddCurrentRow();
        return result;

        void AddCurrentRow()
        {
            if (currentRow.Count == 0)
            {
                return;
            }

            var orderedWords = currentRow
                .OrderBy(word => word.BoundingBox.Left)
                .Where(word => !string.IsNullOrWhiteSpace(word.Text))
                .ToArray();

            var builder = new StringBuilder();
            Word? previousWord = null;

            foreach (var word in orderedWords)
            {
                if (previousWord is not null)
                {
                    var horizontalGap = word.BoundingBox.Left - previousWord.BoundingBox.Right;
                    builder.Append(horizontalGap > 14 ? "   " : " ");
                }

                builder.Append(word.Text.Trim());
                previousWord = word;
            }

            var line = builder.ToString().Trim();

            if (!string.IsNullOrWhiteSpace(line))
            {
                result.Add(line);
            }
        }
    }

    private static List<ExtractedTable> TryParsePipeDelimitedTables(IReadOnlyCollection<string> lines)
    {
        var result = new List<ExtractedTable>();
        var currentHeaders = Array.Empty<string>();
        var currentRows = new List<PdfRowBuffer>();
        var tableIndex = 1;
        var rowNumber = 1;

        foreach (var rawLine in lines)
        {
            var line = NormalizeLine(rawLine);

            if (string.IsNullOrWhiteSpace(line) || IsNoiseLine(line))
            {
                continue;
            }

            var headerStart = IndexOfHeaderStart(line);
            if (headerStart > 0)
            {
                line = line[headerStart..].Trim();
            }

            if (!line.Contains('|'))
            {
                AppendContinuationToPreviousRow(line);
                continue;
            }

            var parts = SplitPipeLine(line);

            if (parts.Length < 2)
            {
                AppendContinuationToPreviousRow(line);
                continue;
            }

            if (LooksLikeHeader(parts))
            {
                var headers = parts.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();

                // Repeated headers appear on each PDF page. Keep accumulating into the same table
                // when the header is the same; otherwise flush and start a new table.
                if (currentHeaders.Length > 0 && SameHeaders(currentHeaders, headers))
                {
                    continue;
                }

                FlushCurrentTable();
                currentHeaders = headers;
                currentRows = [];
                rowNumber = 1;
                continue;
            }

            if (currentHeaders.Length == 0)
            {
                continue;
            }

            if (parts.Length < currentHeaders.Length)
            {
                AppendContinuationToPreviousRow(line);
                continue;
            }

            var values = new Dictionary<string, string?>();

            for (var i = 0; i < currentHeaders.Length; i++)
            {
                string? value;

                if (i == currentHeaders.Length - 1 && parts.Length > currentHeaders.Length)
                {
                    value = string.Join(" ", parts.Skip(i)).Trim();
                }
                else
                {
                    value = i < parts.Length ? parts[i] : null;
                }

                values[currentHeaders[i]] = string.IsNullOrWhiteSpace(value) ? null : value;
            }

            if (values.Values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            currentRows.Add(new PdfRowBuffer(rowNumber + 1, values));
            rowNumber++;
        }

        FlushCurrentTable();
        return result;

        void AppendContinuationToPreviousRow(string continuation)
        {
            if (currentHeaders.Length == 0 || currentRows.Count == 0)
            {
                return;
            }

            var lastHeader = currentHeaders[^1];
            var lastRow = currentRows[^1];
            var existingValue = lastRow.Values.TryGetValue(lastHeader, out var value) ? value : null;

            lastRow.Values[lastHeader] = string.IsNullOrWhiteSpace(existingValue)
                ? continuation.Trim()
                : $"{existingValue.Trim()} {continuation.Trim()}";
        }

        void FlushCurrentTable()
        {
            if (currentHeaders.Length == 0)
            {
                return;
            }

            var rows = currentRows
                .Select(row => new ExtractedRow(
                    row.RowNumber,
                    row.Values,
                    JsonSerializer.Serialize(row.Values)
                ))
                .ToArray();

            result.Add(
                new ExtractedTable(
                    $"PDF Table {tableIndex}",
                    currentHeaders,
                    rows
                )
            );

            tableIndex++;
            currentHeaders = [];
            currentRows = [];
        }
    }

    private static List<ExtractedTable> TryParseFclCellStreamTables(IReadOnlyCollection<string> lines)
    {
        var expandedLines = lines
            .SelectMany(ExpandLineForCellStream)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        if (expandedLines.Length == 0)
        {
            return [];
        }

        var headerStart = -1;
        var headerEnd = -1;
        var headers = new List<string>();

        for (var i = 0; i < expandedLines.Length; i++)
        {
            var token = expandedLines[i];

            if (!IsKnownFclHeaderToken(token))
            {
                if (headers.Count > 0 && !HasMinimumFclHeader(headers))
                {
                    headerStart = -1;
                    headers.Clear();
                }

                continue;
            }

            headerStart = headerStart < 0 ? i : headerStart;
            headers.Add(NormalizeFclHeaderToken(token));

            if (!HasMinimumFclHeader(headers))
            {
                continue;
            }

            headerEnd = i + 1;

            while (headerEnd < expandedLines.Length && IsKnownFclHeaderToken(expandedLines[headerEnd]))
            {
                headers.Add(NormalizeFclHeaderToken(expandedLines[headerEnd]));
                headerEnd++;
            }

            break;
        }

        if (headerStart < 0 || headerEnd < 0 || !HasMinimumFclHeader(headers))
        {
            return [];
        }

        headers = NormalizeCompoundHeaders(headers).ToList();

        var rawDataCells = new List<string>();

        for (var i = headerEnd; i < expandedLines.Length; i++)
        {
            var line = expandedLines[i];

            if (IsNoiseLine(line) || IsKnownFclHeaderToken(line))
            {
                continue;
            }

            rawDataCells.Add(line);
        }

        var rows = new List<ExtractedRow>();
        var index = 0;
        var rowNumber = 2;

        while (index < rawDataCells.Count)
        {
            var remaining = rawDataCells.Skip(index).ToArray();
            var rowCells = TakeFclRowCells(headers, remaining, out var consumed);

            if (rowCells.Count == 0 || consumed <= 0)
            {
                index++;
                continue;
            }

            if (!LooksLikeFclDataRow(headers, rowCells))
            {
                index += consumed;
                continue;
            }

            var values = new Dictionary<string, string?>();

            for (var i = 0; i < headers.Count; i++)
            {
                values[headers[i]] = i < rowCells.Count && !string.IsNullOrWhiteSpace(rowCells[i])
                    ? rowCells[i]
                    : null;
            }

            rows.Add(new ExtractedRow(rowNumber, values, JsonSerializer.Serialize(values)));
            rowNumber++;
            index += consumed;
        }

        return rows.Count == 0
            ? []
            : [new ExtractedTable("PDF FCL Cell Stream", headers, rows)];
    }

    private static IReadOnlyCollection<string> ExpandLineForCellStream(string rawLine)
    {
        var line = NormalizeLine(rawLine);

        if (string.IsNullOrWhiteSpace(line) || IsNoiseLine(line))
        {
            return [];
        }

        var headerTokens = TrySplitFclHeaderLine(line);

        if (headerTokens.Count > 0)
        {
            return headerTokens;
        }

        return [line];
    }

    private static IReadOnlyCollection<string> TrySplitFclHeaderLine(string line)
    {
        var knownHeaders = new[]
        {
            "Port of Loading",
            "Port of Discharge",
            "Origin Port",
            "Destination Port",
            "Origen",
            "Destino",
            "POL",
            "POD",
            "Carrier",
            "Naviera",
            "Shipping Line",
            "20GP",
            "20DV",
            "20DC",
            "20FT",
            "20'",
            "40/40HC",
            "40'/40HC",
            "40GP/40HC",
            "40GP",
            "40DV",
            "40DC",
            "40FT",
            "40'",
            "40HC",
            "40HQ",
            "45HC",
            "Free Time",
            "Free Days",
            "Effective",
            "Effective Date",
            "Valid From",
            "Expiry",
            "Expiration",
            "Valid To",
            "Validity",
            "Currency",
            "Moneda"
        };

        var matches = knownHeaders
            .Select(header => new
            {
                Header = header,
                Match = Regex.Match(
                    line,
                    $@"(?<![A-Za-z0-9]){Regex.Escape(header)}(?![A-Za-z0-9])",
                    RegexOptions.IgnoreCase
                )
            })
            .Where(x => x.Match.Success)
            .OrderBy(x => x.Match.Index)
            .ThenByDescending(x => x.Header.Length)
            .ToList();

        if (matches.Count == 0)
        {
            return [];
        }

        var result = new List<string>();
        var lastEnd = -1;

        foreach (var item in matches)
        {
            if (item.Match.Index < lastEnd)
            {
                continue;
            }

            result.Add(item.Header);
            lastEnd = item.Match.Index + item.Match.Length;
        }

        return result.Count > 0 && result.All(IsKnownFclHeaderToken) ? result : [];
    }

    private static IReadOnlyCollection<string> NormalizeCompoundHeaders(IReadOnlyCollection<string> headers)
    {
        var result = new List<string>();

        foreach (var header in headers)
        {
            var normalized = ColumnHeaderNormalizer.Normalize(header);

            if (normalized is "free" && result.Count > 0 && ColumnHeaderNormalizer.Normalize(result[^1]) is "time")
            {
                result[^1] = "Free Time";
                continue;
            }

            if (normalized is "time" && result.Count > 0 && ColumnHeaderNormalizer.Normalize(result[^1]) is "free")
            {
                result[^1] = "Free Time";
                continue;
            }

            result.Add(header);
        }

        return result;
    }

    private static IReadOnlyList<string> TakeFclRowCells(
        IReadOnlyList<string> headers,
        IReadOnlyList<string> sourceCells,
        out int consumed
    )
    {
        consumed = 0;

        if (headers.Count == 0 || sourceCells.Count == 0)
        {
            return [];
        }

        var expanded = new List<string>();

        while (consumed < sourceCells.Count && expanded.Count < headers.Count)
        {
            var cell = sourceCells[consumed];
            var splitCells = TrySplitFclDataLine(cell, headers.Count);

            if (splitCells.Count > 1)
            {
                expanded.AddRange(splitCells);
                consumed++;
                continue;
            }

            expanded.Add(cell);
            consumed++;
        }

        var normalized = NormalizeFclDataCells(headers, expanded);

        if (normalized.Count < headers.Count)
        {
            return [];
        }

        if (normalized.Count > headers.Count)
        {
            normalized = normalized.Take(headers.Count - 1)
                .Concat([string.Join(" ", normalized.Skip(headers.Count - 1))])
                .ToList();
        }

        return normalized;
    }

    private static IReadOnlyList<string> TrySplitFclDataLine(string line, int expectedColumnCount)
    {
        if (!line.Contains('$') && !Regex.IsMatch(line, @"\b\d{1,3}(?:,\d{3})+(?:\.\d+)?\b"))
        {
            return [];
        }

        var parts = Regex.Split(line.Trim(), @"\s{2,}")
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        if (parts.Length >= expectedColumnCount)
        {
            return parts.ToList();
        }

        parts = Regex.Split(line.Trim(), @"\s+")
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        return parts.Length >= Math.Min(expectedColumnCount, 5) ? parts.ToList() : [];
    }

    private static IReadOnlyList<string> NormalizeFclDataCells(
        IReadOnlyList<string> headers,
        IReadOnlyCollection<string> sourceCells
    )
    {
        var cells = sourceCells.ToList();

        for (var i = 0; i < headers.Count && i < cells.Count - 1; i++)
        {
            var target = GetTargetFieldForHeader(headers[i]);
            var next = cells[i + 1];

            if (target == "FreeDays" && IsFreeDaysUnit(next))
            {
                cells[i] = $"{cells[i]} {next}".Trim();
                cells.RemoveAt(i + 1);
            }
        }

        return cells;
    }

    private static bool LooksLikeFclDataRow(IReadOnlyList<string> headers, IReadOnlyList<string> rowCells)
    {
        if (rowCells.Count < headers.Count)
        {
            return false;
        }

        var hasOrigin = false;
        var hasDestination = false;
        var hasCarrier = false;
        var hasAmount = false;

        for (var i = 0; i < headers.Count; i++)
        {
            var target = GetTargetFieldForHeader(headers[i]);
            var value = rowCells[i];

            hasOrigin |= target == "OriginPort" && !string.IsNullOrWhiteSpace(value);
            hasDestination |= target == "DestinationPort" && !string.IsNullOrWhiteSpace(value);
            hasCarrier |= target == "Carrier" && !string.IsNullOrWhiteSpace(value);
            hasAmount |= IsContainerAmountHeader(headers[i]) && !string.IsNullOrWhiteSpace(value);
        }

        return hasOrigin && hasDestination && hasCarrier && hasAmount;
    }

    private static bool HasMinimumFclHeader(IReadOnlyCollection<string> headers)
    {
        var targets = headers.Select(GetTargetFieldForHeader).ToArray();

        return targets.Contains("OriginPort")
            && targets.Contains("DestinationPort")
            && targets.Contains("Carrier")
            && headers.Any(IsContainerAmountHeader);
    }

    private static bool IsKnownFclHeaderToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(GetTargetFieldForHeader(token))
            || IsContainerAmountHeader(token);
    }

    private static string NormalizeFclHeaderToken(string token)
    {
        var clean = token.Trim();
        var normalized = ColumnHeaderNormalizer.Normalize(clean);

        return normalized switch
        {
            "origen" or "origin" or "originport" or "portofloading" => "POL",
            "destino" or "destination" or "destinationport" or "portofdischarge" => "POD",
            "shippingline" or "naviera" or "carrier" => "Carrier",
            "freetime" or "freedays" => "Free Time",
            "effective" or "effectivedate" or "validfrom" => "Effective",
            "expiry" or "expiration" or "validto" or "validity" => "Expiry",
            _ => clean
        };
    }

    private static string? GetTargetFieldForHeader(string? header)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        var normalized = ColumnHeaderNormalizer.Normalize(header);

        if (DefaultFclColumnMappings.Mappings.TryGetValue(normalized, out var targetField))
        {
            return targetField;
        }

        if (IsContainerAmountHeader(header))
        {
            return "OceanFreight";
        }

        return null;
    }

    private static bool IsFreeDaysUnit(string value)
    {
        var normalized = ColumnHeaderNormalizer.Normalize(value);
        return normalized is "dia" or "dias" or "day" or "days";
    }

    private static List<ExtractedTable> TryParseWhitespaceDelimitedTables(IReadOnlyCollection<string> lines)
    {
        var result = new List<ExtractedTable>();
        var currentHeaders = Array.Empty<string>();
        var currentRows = new List<PdfRowBuffer>();
        var tableIndex = 1;
        var rowNumber = 1;

        foreach (var rawLine in lines)
        {
            var line = NormalizeLine(rawLine);

            if (string.IsNullOrWhiteSpace(line) || IsNoiseLine(line) || line.Contains('|'))
            {
                continue;
            }

            var headerStart = IndexOfHeaderStart(line);
            if (headerStart > 0)
            {
                line = line[headerStart..].Trim();
            }

            var parts = SplitWhitespaceLine(line);

            if (parts.Length < 2)
            {
                AppendContinuationToPreviousRow(line);
                continue;
            }

            if (LooksLikeHeader(parts))
            {
                var headers = parts.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();

                if (currentHeaders.Length > 0 && SameHeaders(currentHeaders, headers))
                {
                    continue;
                }

                FlushCurrentTable();
                currentHeaders = headers;
                currentRows = [];
                rowNumber = 1;
                continue;
            }

            if (currentHeaders.Length == 0)
            {
                continue;
            }

            var values = BuildValues(currentHeaders, SplitWhitespaceLineForRow(line, currentHeaders.Length));

            if (values.Count == 0 || values.Values.All(string.IsNullOrWhiteSpace))
            {
                AppendContinuationToPreviousRow(line);
                continue;
            }

            currentRows.Add(new PdfRowBuffer(rowNumber + 1, values));
            rowNumber++;
        }

        FlushCurrentTable();
        return result;

        void AppendContinuationToPreviousRow(string continuation)
        {
            if (currentHeaders.Length == 0 || currentRows.Count == 0)
            {
                return;
            }

            var lastHeader = currentHeaders[^1];
            var lastRow = currentRows[^1];
            var existingValue = lastRow.Values.TryGetValue(lastHeader, out var value) ? value : null;

            lastRow.Values[lastHeader] = string.IsNullOrWhiteSpace(existingValue)
                ? continuation.Trim()
                : $"{existingValue.Trim()} {continuation.Trim()}";
        }

        void FlushCurrentTable()
        {
            if (currentHeaders.Length == 0)
            {
                return;
            }

            var rows = currentRows
                .Select(row => new ExtractedRow(
                    row.RowNumber,
                    row.Values,
                    JsonSerializer.Serialize(row.Values)
                ))
                .ToArray();

            result.Add(new ExtractedTable($"PDF Visual Table {tableIndex}", currentHeaders, rows));

            tableIndex++;
            currentHeaders = [];
            currentRows = [];
        }
    }

    private static Dictionary<string, string?> BuildValues(
        IReadOnlyList<string> headers,
        IReadOnlyList<string> parts
    )
    {
        var values = new Dictionary<string, string?>();

        if (headers.Count == 0 || parts.Count == 0)
        {
            return values;
        }

        for (var i = 0; i < headers.Count; i++)
        {
            string? value;

            if (i == headers.Count - 1 && parts.Count > headers.Count)
            {
                value = string.Join(" ", parts.Skip(i)).Trim();
            }
            else
            {
                value = i < parts.Count ? parts[i] : null;
            }

            values[headers[i]] = string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return values;
    }

    private static string[] SplitWhitespaceLine(string line)
    {
        var parts = Regex.Split(line.Trim(), @"\s{2,}")
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        if (parts.Length >= 2)
        {
            return parts;
        }

        return Regex.Split(line.Trim(), @"\s+")
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
    }

    private static string[] SplitWhitespaceLineForRow(string line, int expectedColumnCount)
    {
        var parts = Regex.Split(line.Trim(), @"\s{2,}")
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

        if (parts.Length >= Math.Min(expectedColumnCount, 2))
        {
            return parts;
        }

        return Regex.Split(line.Trim(), @"\s+")
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
    }

    private static string[] SplitPipeLine(string line)
    {
        return line
            .Split('|')
            .Select(x => x.Trim())
            .ToArray();
    }

    private static bool LooksLikeHeader(IReadOnlyCollection<string> parts)
    {
        if (parts.Count < 2)
        {
            return false;
        }

        var knownFieldCount = parts.Count(part =>
        {
            var normalized = ColumnHeaderNormalizer.Normalize(part);

            return DefaultFclColumnMappings.Mappings.ContainsKey(normalized)
                || IsContainerAmountHeader(part);
        });

        return knownFieldCount >= 2;
    }

    private static bool IsContainerAmountHeader(string? header)
    {
        var normalized = ColumnHeaderNormalizer.Normalize(header);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        if (normalized.Contains("40hc") || normalized.Contains("40hq") || normalized.Contains("40highcube"))
        {
            return true;
        }

        return Regex.IsMatch(
            normalized,
            @"^(20|20gp|20dc|20dv|20std|20ft|20dry|40|40gp|40dc|40dv|40std|40ft|40dry|45hc|45hq)(usd|eur|crc|rate|rates|freight|flete|tarifa|amount|costo|venta|allin|oceanfreight)?$"
        );
    }

    private static bool SameHeaders(IReadOnlyCollection<string> left, IReadOnlyCollection<string> right)
    {
        return left.Count == right.Count
            && left.Zip(right).All(pair => string.Equals(
                pair.First,
                pair.Second,
                StringComparison.OrdinalIgnoreCase
            ));
    }

    private static int IndexOfHeaderStart(string line)
    {
        var index = line.IndexOf("AGENTE | POL", StringComparison.OrdinalIgnoreCase);
        return index >= 0 ? index : -1;
    }

    private static bool IsNoiseLine(string line)
    {
        return line.StartsWith("MATRIZ COSTOS", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Tarifas", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Pagina ", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Página ", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Page ", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Confidential", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLine(string line)
    {
        return line
            .Replace("¦", "|")
            .Replace("│", "|")
            .Replace("┃", "|")
            .Replace("\t", " | ")
            .Replace("  |", " |")
            .Replace("|  ", "| ")
            .Trim();
    }

    private sealed record PdfRowBuffer(
        int RowNumber,
        Dictionary<string, string?> Values
    );
}
