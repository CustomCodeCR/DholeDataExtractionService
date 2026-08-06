using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dhole.DataExtraction.Infrastructure.Mapping;
using Dhole.DataExtraction.Infrastructure.Files;
using Dhole.DataExtraction.Infrastructure.Normalization;
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
        var pages = document.GetPages().ToArray();

        var lines = new List<string>();

        foreach (var page in pages)
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
        var tables = TryParseVisualCarrierTariffTables(pages, rawText);

        if (tables.Count == 0)
        {
            tables = TryParseAlignedFclMatrixTables(normalizedLines);
        }

        if (tables.Count == 0)
        {
            tables = TryParseFclCellStreamTables(normalizedLines);
        }

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


    /// <summary>
    /// Parses visually aligned carrier tariff matrices where the carrier is shown only
    /// in the document branding (for example a PIL/AGUNSA PDF) and the first column is
    /// an optional merged region. Generic whitespace parsing shifts those rows because
    /// most data lines do not repeat the merged region value.
    /// </summary>
    private static List<ExtractedTable> TryParseVisualCarrierTariffTables(
        IReadOnlyCollection<Page> pages,
        string rawText
    )
    {
        var carrier = InferCarrierFromDocument(rawText);
        if (string.IsNullOrWhiteSpace(carrier))
        {
            return [];
        }

        var freeDays = InferGlobalFreeDays(rawText);
        var resultRows = new List<ExtractedRow>();
        var rowNumber = 2;
        IReadOnlyList<string>? amountHeaders = null;

        foreach (var page in pages)
        {
            var visualRows = GroupWordsByVisualRow(page);
            var headerIndex = -1;
            VisualTariffHeader? header = null;

            for (var index = 0; index < visualRows.Count; index++)
            {
                header = TryBuildVisualTariffHeader(visualRows[index]);
                if (header is null)
                {
                    continue;
                }

                headerIndex = index;
                amountHeaders ??= header.AmountColumns
                    .Select(column => column.Header)
                    .ToArray();
                break;
            }

            if (headerIndex < 0 || header is null)
            {
                continue;
            }

            foreach (var visualRow in visualRows.Skip(headerIndex + 1))
            {
                var values = TryReadVisualTariffRow(visualRow, header, carrier, freeDays);
                if (values is null)
                {
                    continue;
                }

                resultRows.Add(
                    new ExtractedRow(
                        rowNumber++,
                        values,
                        JsonSerializer.Serialize(values)
                    )
                );
            }
        }

        if (resultRows.Count == 0 || amountHeaders is null)
        {
            return [];
        }

        var headers = new List<string>
        {
            "POL",
            "POE",
            "Carrier",
            "Currency",
        };
        headers.AddRange(amountHeaders);
        if (!string.IsNullOrWhiteSpace(freeDays))
        {
            headers.Add("Free Time");
        }
        headers.Add("Validity");

        return
        [
            new ExtractedTable(
                "PDF Carrier Tariff Matrix",
                headers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                resultRows
            ),
        ];
    }

    private static IReadOnlyList<VisualWordRow> GroupWordsByVisualRow(Page page)
    {
        var words = page.GetWords()
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .OrderByDescending(word => word.BoundingBox.Bottom)
            .ThenBy(word => word.BoundingBox.Left)
            .ToArray();
        var rows = new List<VisualWordRow>();
        var current = new List<Word>();
        double? currentY = null;

        foreach (var word in words)
        {
            var y = word.BoundingBox.Bottom;
            if (currentY is null || Math.Abs(currentY.Value - y) <= RowTolerance)
            {
                current.Add(word);
                currentY ??= y;
                continue;
            }

            AddCurrent();
            current = [word];
            currentY = y;
        }

        AddCurrent();
        return rows;

        void AddCurrent()
        {
            if (current.Count == 0 || currentY is null)
            {
                return;
            }

            rows.Add(
                new VisualWordRow(
                    currentY.Value,
                    current.OrderBy(word => word.BoundingBox.Left).ToArray()
                )
            );
        }
    }

    private static VisualTariffHeader? TryBuildVisualTariffHeader(VisualWordRow row)
    {
        var originStart = FindPhraseStart(
            row.Words,
            ["puerto", "de", "origen"],
            ["origin", "port"],
            ["port", "of", "loading"],
            ["pol"]
        );
        var destinationStart = FindPhraseStart(
            row.Words,
            ["puerto", "destino"],
            ["destination", "port"],
            ["port", "of", "discharge"],
            ["pod"],
            ["poe"]
        );
        var validityStart = FindPhraseStart(
            row.Words,
            ["validity", "date"],
            ["validity"],
            ["vigencia"],
            ["fecha", "vigencia"]
        );
        var carrierStart = FindPhraseStart(
            row.Words,
            ["carrier"],
            ["naviera"],
            ["shipping", "line"]
        );

        // This parser is specifically for matrices whose carrier is not repeated as
        // a column. Tables with an explicit carrier belong to the generic FCL parser.
        if (
            originStart is null
            || destinationStart is null
            || validityStart is null
            || carrierStart is not null
        )
        {
            return null;
        }

        var amountColumns = row.Words
            .Where(word => PricingContainerVariants.Expand(word.Text).Count > 0)
            .Select(word => new VisualAmountColumn(
                word.BoundingBox.Left,
                CanonicalVisualContainerHeader(word.Text)
            ))
            .OrderBy(column => column.Start)
            .ToArray();
        if (amountColumns.Length < 2)
        {
            return null;
        }

        if (
            originStart.Value >= destinationStart.Value
            || destinationStart.Value >= amountColumns[0].Start
            || amountColumns[^1].Start >= validityStart.Value
        )
        {
            return null;
        }

        var regionStart = FindPhraseStart(
            row.Words,
            ["region"],
            ["región"]
        );

        return new VisualTariffHeader(
            regionStart,
            originStart.Value,
            destinationStart.Value,
            amountColumns,
            validityStart.Value
        );
    }

    private static Dictionary<string, string?>? TryReadVisualTariffRow(
        VisualWordRow row,
        VisualTariffHeader header,
        string carrier,
        string? freeDays
    )
    {
        var originLower = header.RegionStart.HasValue
            ? Midpoint(header.RegionStart.Value, header.OriginStart)
            : double.MinValue;
        var originUpper = Midpoint(header.OriginStart, header.DestinationStart);
        var destinationUpper = Midpoint(
            header.DestinationStart,
            header.AmountColumns[0].Start
        );
        var validityLower = Midpoint(
            header.AmountColumns[^1].Start,
            header.ValidityStart
        );

        var originWords = row.Words.Where(word =>
            Center(word) >= originLower && Center(word) < originUpper
        );
        var destinationWords = row.Words.Where(word =>
            Center(word) >= originUpper && Center(word) < destinationUpper
        );
        var origin = JoinVisualWords(originWords);
        var destination = JoinVisualWords(destinationWords);
        var validity = JoinVisualWords(row.Words.Where(word => Center(word) >= validityLower));

        if (
            string.IsNullOrWhiteSpace(origin)
            || string.IsNullOrWhiteSpace(destination)
            || !ContainsDateRange(validity)
        )
        {
            return null;
        }

        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["POL"] = origin,
            ["POE"] = destination,
            ["Carrier"] = carrier,
            ["Currency"] = "USD",
            ["Validity"] = validity,
        };

        var amountCount = 0;
        for (var index = 0; index < header.AmountColumns.Count; index++)
        {
            var lower = index == 0
                ? destinationUpper
                : Midpoint(
                    header.AmountColumns[index - 1].Start,
                    header.AmountColumns[index].Start
                );
            var upper = index == header.AmountColumns.Count - 1
                ? validityLower
                : Midpoint(
                    header.AmountColumns[index].Start,
                    header.AmountColumns[index + 1].Start
                );
            var amount = JoinVisualWords(row.Words.Where(word =>
                Center(word) >= lower && Center(word) < upper
            ));

            if (MoneyNormalizer.Normalize(amount) is null)
            {
                continue;
            }

            values[header.AmountColumns[index].Header] = amount;
            amountCount++;
        }

        if (amountCount == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(freeDays))
        {
            values["Free Time"] = freeDays;
        }

        return values;
    }

    private static double? FindPhraseStart(
        IReadOnlyList<Word> words,
        params string[][] alternatives
    )
    {
        var normalizedWords = words
            .Select(word => ColumnHeaderNormalizer.Normalize(word.Text))
            .ToArray();

        foreach (var phrase in alternatives)
        {
            var normalizedPhrase = phrase
                .Select(ColumnHeaderNormalizer.Normalize)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();
            if (normalizedPhrase.Length == 0 || normalizedPhrase.Length > words.Count)
            {
                continue;
            }

            for (var start = 0; start + normalizedPhrase.Length <= words.Count; start++)
            {
                var matches = true;
                for (var offset = 0; offset < normalizedPhrase.Length; offset++)
                {
                    if (!string.Equals(
                        normalizedWords[start + offset],
                        normalizedPhrase[offset],
                        StringComparison.OrdinalIgnoreCase
                    ))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    return words[start].BoundingBox.Left;
                }
            }
        }

        return null;
    }

    private static string CanonicalVisualContainerHeader(string value)
    {
        var variants = PricingContainerVariants.Expand(value);
        return variants.Count > 1 ? string.Join('/', variants) : value.Trim();
    }

    private static string? InferCarrierFromDocument(string rawText)
    {
        var carrierPatterns = new (string Pattern, string Carrier, bool IgnoreCase)[]
        {
            (@"\bCMA\s*CGM\b", "CMA CGM", true),
            (@"\bHAPAG(?:-|\s*)LLOYD\b", "HAPAG-LLOYD", true),
            (@"\bEVERGREEN\b", "EVERGREEN", true),
            (@"\bMAERSK\b", "MAERSK", true),
            (@"\bCOSCO\b", "COSCO", false),
            (@"\bOOCL\b", "OOCL", false),
            (@"\bMSC\b", "MSC", false),
            (@"\bPIL\b", "PIL", false),
            (@"\bONE\b", "ONE", false),
            (@"\bWHL\b", "WHL", false),
        };

        var matches = carrierPatterns
            .Where(item => Regex.IsMatch(
                rawText,
                item.Pattern,
                item.IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None
            ))
            .Select(item => item.Carrier)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return matches.Length == 1 ? matches[0] : null;
    }

    private static string? InferGlobalFreeDays(string rawText)
    {
        var match = Regex.Match(
            rawText,
            @"(?:tiempo\s+libre|free\s*time|free\s*days)[^\d]{0,100}(?<days>\d{1,3})\s*(?:d[ií]as?|days?)",
            RegexOptions.IgnoreCase
        );
        if (!match.Success)
        {
            return null;
        }

        var days = match.Groups["days"].Value;
        return $"{days} days";
    }

    private static bool ContainsDateRange(string value)
    {
        return Regex.Matches(
            value,
            @"\b\d{1,2}[/.-]\d{1,2}[/.-]\d{2,4}\b",
            RegexOptions.IgnoreCase
        ).Count >= 2;
    }

    private static string JoinVisualWords(IEnumerable<Word> words)
    {
        return string.Join(
            " ",
            words.OrderBy(word => word.BoundingBox.Left)
                .Select(word => word.Text.Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
        ).Trim();
    }

    private static double Center(Word word)
    {
        return (word.BoundingBox.Left + word.BoundingBox.Right) / 2d;
    }

    private static double Midpoint(double left, double right)
    {
        return left + ((right - left) / 2d);
    }

    private static List<ExtractedTable> TryParseAlignedFclMatrixTables(
        IReadOnlyCollection<string> lines
    )
    {
        var lineArray = lines.ToArray();

        for (var headerIndex = 0; headerIndex < lineArray.Length; headerIndex++)
        {
            var rawHeaders = SplitAlignedLine(lineArray[headerIndex]);
            if (!HasMinimumFclHeader(rawHeaders))
            {
                continue;
            }

            var headers = rawHeaders.Select(NormalizeFclHeaderToken).ToArray();
            var rows = new List<ExtractedRow>();
            var rowNumber = 2;

            for (var rowIndex = headerIndex + 1; rowIndex < lineArray.Length; rowIndex++)
            {
                var line = NormalizeLine(lineArray[rowIndex]);

                if (string.IsNullOrWhiteSpace(line) || IsNoiseLine(line))
                {
                    continue;
                }

                if (IsTableTerminatorLine(line))
                {
                    break;
                }

                var fields = SplitAlignedLine(line);
                if (fields.Length == 0)
                {
                    continue;
                }

                if (HasMinimumFclHeader(fields))
                {
                    continue;
                }

                if (fields.Length < headers.Length)
                {
                    if (rows.Count > 0)
                    {
                        break;
                    }

                    continue;
                }

                IReadOnlyList<string> rowCells = fields;
                if (fields.Length > headers.Length)
                {
                    rowCells = fields.Take(headers.Length - 1)
                        .Concat([string.Join(" ", fields.Skip(headers.Length - 1))])
                        .ToArray();
                }

                if (!LooksLikeFclDataRow(headers, rowCells))
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
                    values[headers[columnIndex]] = rowCells[columnIndex];
                }

                rows.Add(new ExtractedRow(rowNumber, values, JsonSerializer.Serialize(values)));
                rowNumber++;
            }

            if (rows.Count > 0)
            {
                return [new ExtractedTable("PDF FCL Aligned Matrix", headers, rows)];
            }
        }

        return [];
    }

    private static string[] SplitAlignedLine(string line)
    {
        return Regex.Split(NormalizeLine(line), @"(?:\t+|\s{2,})")
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();
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
            "Días libres",
            "Dias libres",
            "Effective",
            "Effective Date",
            "Valid From",
            "Vigencia",
            "Vigencia desde",
            "Inicio",
            "Fecha inicio",
            "Start Date",
            "Expiry",
            "Expiration",
            "Expiración",
            "Expiracion",
            "Valid To",
            "Validity",
            "Vence",
            "Vencimiento",
            "Fecha fin",
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
            hasAmount |= IsContainerAmountHeader(headers[i])
                && MoneyNormalizer.Normalize(value) is not null;
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
            "destino" or "destination" or "destinationport" or "portofdischarge" => "POE",
            "shippingline" or "naviera" or "carrier" => "Carrier",
            "freetime" or "freedays" or "diaslibres" => "Free Time",
            "effective" or "effectivedate" or "validfrom" or "vigencia" or "vigenciadesde" or "inicio" or "fechainicio" or "start" or "startdate" => "Effective",
            "expiry" or "expiration" or "expirationdate" or "expiracion" or "validto" or "validity" or "vence" or "vencimiento" or "fechavencimiento" or "fin" or "fechafin" => "Expiry",
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

    private static bool IsTableTerminatorLine(string line)
    {
        return line.StartsWith("Condiciones", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Alcance", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Observación", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Observacion", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Terms", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Remarks", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Documento de ejemplo", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Generado el", StringComparison.OrdinalIgnoreCase);
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
        return TextContentDecoder.Clean(line)
            .Replace("¦", "|")
            .Replace("│", "|")
            .Replace("┃", "|")
            .Replace("\t", " | ")
            .Replace("  |", " |")
            .Replace("|  ", "| ")
            .Trim();
    }

    private sealed record VisualWordRow(
        double Y,
        IReadOnlyList<Word> Words
    );

    private sealed record VisualAmountColumn(
        double Start,
        string Header
    );

    private sealed record VisualTariffHeader(
        double? RegionStart,
        double OriginStart,
        double DestinationStart,
        IReadOnlyList<VisualAmountColumn> AmountColumns,
        double ValidityStart
    );

    private sealed record PdfRowBuffer(
        int RowNumber,
        Dictionary<string, string?> Values
    );
}
