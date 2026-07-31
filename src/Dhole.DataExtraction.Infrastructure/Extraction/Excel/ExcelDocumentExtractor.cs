using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using Dhole.DataExtraction.Infrastructure.Mapping;
using Dhole.DataExtraction.Infrastructure.Normalization;

namespace Dhole.DataExtraction.Infrastructure.Extraction.Excel;

public sealed class ExcelDocumentExtractor : IDocumentExtractor
{
    private const int MaxHeaderScanRows = 30;
    private const int MaximumMetadataCells = 800;

    private static readonly IReadOnlyDictionary<string, int> MonthNumbers =
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

    public SourceFileType FileType => SourceFileType.Excel;

    public Task<ExtractedDocument> ExtractAsync(
        DocumentExtractionInput input,
        CancellationToken cancellationToken = default
    )
    {
        using var stream = new MemoryStream(input.FileContent);
        using var workbook = new XLWorkbook(stream);

        if (
            TryExtractCarrierTariffMatrix(
                workbook,
                input.OriginalFileName,
                cancellationToken,
                out var carrierTariffTable
            )
        )
        {
            return Task.FromResult(
                new ExtractedDocument(
                    input.OriginalFileName,
                    SourceFileType.Excel,
                    [carrierTariffTable]
                )
            );
        }

        var tables = new List<ExtractedTable>();

        foreach (var worksheet in workbook.Worksheets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var usedRange = worksheet.RangeUsed();

            if (usedRange is null)
            {
                continue;
            }

            var header = FindHeaderRow(usedRange);
            if (header is null || header.Headers.Count == 0)
            {
                continue;
            }

            var rows = new List<ExtractedRow>();
            var firstDataRowNumber = header.RowNumber + 1;
            var lastRowNumber = usedRange.LastRowUsed().RowNumber();

            for (var rowNumber = firstDataRowNumber; rowNumber <= lastRowNumber; rowNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var row = worksheet.Row(rowNumber);
                var values = new Dictionary<string, string?>();

                foreach (var column in header.Columns)
                {
                    var cellValue = row.Cell(column.ColumnNumber).GetFormattedString()?.Trim();
                    values[column.Header] = string.IsNullOrWhiteSpace(cellValue) ? null : cellValue;
                }

                if (values.Values.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                rows.Add(new ExtractedRow(rowNumber, values));
            }

            tables.Add(new ExtractedTable(worksheet.Name, header.Headers, rows));
        }

        var document = new ExtractedDocument(input.OriginalFileName, SourceFileType.Excel, tables);

        return Task.FromResult(document);
    }

    /// <summary>
    /// Carrier spreadsheets such as "MSC DT CALDERA - Validez 08 al 14 de AGOSTO"
    /// usually keep route metadata in titles and the amounts in a POL matrix. The
    /// generic extractor sees the amounts but loses carrier, destination and validity,
    /// causing every row to be rejected by Pricing. This path converts that layout to
    /// a clean FCL matrix before the standard mapping/normalization pipeline runs.
    /// </summary>
    private static bool TryExtractCarrierTariffMatrix(
        XLWorkbook workbook,
        string originalFileName,
        CancellationToken cancellationToken,
        out ExtractedTable table
    )
    {
        table = default!;

        var metadataText = BuildMetadataText(workbook, originalFileName, cancellationToken);
        var carrier = ResolveCarrier(originalFileName, metadataText);
        var portOfExit = ResolvePortOfExit(originalFileName, metadataText);

        if (
            string.IsNullOrWhiteSpace(carrier)
            || string.IsNullOrWhiteSpace(portOfExit)
            || !TryParseValidity(metadataText, out var validFrom, out var validTo)
        )
        {
            return false;
        }

        foreach (var worksheet in workbook.Worksheets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var usedRange = worksheet.RangeUsed();
            if (usedRange is null)
            {
                continue;
            }

            var header = FindHeaderRow(usedRange);
            if (header is null)
            {
                continue;
            }

            var originColumn = header.Columns
                .Where(x => ColumnHeaderNormalizer.Normalize(x.Header) == "pol")
                .OrderBy(x => x.ColumnNumber)
                .FirstOrDefault();

            if (originColumn is null)
            {
                continue;
            }

            // Only use the amount columns that belong to the first POL block. A
            // second block in the same row may contain add-ons (for example
            // "POL Additional TAO") and must not be imported as complete rates.
            var amountColumns = header.Columns
                .Where(x =>
                    x.ColumnNumber > originColumn.ColumnNumber
                    && x.ColumnNumber <= originColumn.ColumnNumber + 3
                    && IsContainerAmountHeader(x.Header)
                )
                .OrderBy(x => x.ColumnNumber)
                .ToArray();

            if (amountColumns.Length == 0)
            {
                continue;
            }

            var extractedRows = new List<ExtractedRow>();
            var firstDataRowNumber = header.RowNumber + 1;
            var lastDataRowNumber = usedRange.LastRowUsed().RowNumber();
            var routeMode = ResolveRouteMode(metadataText);

            for (
                var rowNumber = firstDataRowNumber;
                rowNumber <= lastDataRowNumber;
                rowNumber++
            )
            {
                cancellationToken.ThrowIfCancellationRequested();

                var row = worksheet.Row(rowNumber);
                var originPort = CleanCellText(row.Cell(originColumn.ColumnNumber));

                if (string.IsNullOrWhiteSpace(originPort))
                {
                    continue;
                }

                var amountValues = amountColumns
                    .Select(column => new
                    {
                        column.Header,
                        Value = CleanCellText(row.Cell(column.ColumnNumber)),
                    })
                    .Where(x =>
                        !string.IsNullOrWhiteSpace(x.Value)
                        && MoneyNormalizer.Normalize(x.Value) is not null
                    )
                    .ToArray();

                if (amountValues.Length == 0)
                {
                    continue;
                }

                var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["POL"] = originPort,
                    ["POE"] = portOfExit,
                    ["Carrier"] = carrier,
                    ["Currency"] = "USD",
                    ["ValidFrom"] = validFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["ValidTo"] = validTo.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["RouteMode"] = routeMode,
                    ["Remarks"] = BuildRemarks(routeMode, portOfExit),
                };

                foreach (var amountValue in amountValues)
                {
                    values[amountValue.Header] = amountValue.Value;
                }

                extractedRows.Add(new ExtractedRow(rowNumber, values));
            }

            if (extractedRows.Count == 0)
            {
                continue;
            }

            var headers = new List<string>
            {
                "POL",
                "POE",
                "Carrier",
                "Currency",
                "ValidFrom",
                "ValidTo",
            };
            headers.AddRange(amountColumns.Select(x => x.Header));
            headers.Add("RouteMode");
            headers.Add("Remarks");

            table = new ExtractedTable(
                $"{worksheet.Name} - FCL normalizado",
                headers.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                extractedRows
            );
            return true;
        }

        return false;
    }

    private static string BuildMetadataText(
        XLWorkbook workbook,
        string originalFileName,
        CancellationToken cancellationToken
    )
    {
        var values = new List<string> { originalFileName };
        var readCells = 0;

        foreach (var worksheet in workbook.Worksheets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            values.Add(worksheet.Name);

            var usedRange = worksheet.RangeUsed();
            if (usedRange is null)
            {
                continue;
            }

            foreach (var cell in usedRange.CellsUsed())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var value = CleanCellText(cell);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value);
                }

                readCells++;
                if (readCells >= MaximumMetadataCells)
                {
                    return string.Join('\n', values);
                }
            }
        }

        return string.Join('\n', values);
    }

    private static string? ResolveCarrier(string originalFileName, string metadataText)
    {
        var fileCarrier = MatchCarrier(originalFileName);
        return fileCarrier ?? MatchCarrier(metadataText);
    }

    private static string? MatchCarrier(string text)
    {
        var candidates = new (string Pattern, string Name)[]
        {
            (@"\bMSC\b", "MSC"),
            (@"\bONE\b", "ONE"),
            (@"\b(?:MAERSK|MSK)\b", "Maersk"),
            (@"\bPIL\b", "PIL"),
            (@"\bCOSCO\b", "COSCO"),
            (@"\b(?:HAPAG(?:-LLOYD)?|HPL)\b", "Hapag-Lloyd"),
            (@"\bCMA\s*CGM\b", "CMA CGM"),
            (@"\bEVERGREEN\b", "Evergreen"),
            (@"\bOOCL\b", "OOCL"),
            (@"\b(?:WAN\s*HAI|WHL)\b", "Wan Hai"),
            (@"\bZIM\b", "ZIM"),
        };

        return candidates
            .FirstOrDefault(x => Regex.IsMatch(text, x.Pattern, RegexOptions.IgnoreCase))
            .Name;
    }

    private static string? ResolvePortOfExit(string originalFileName, string metadataText)
    {
        var source = string.Concat(originalFileName, "\n", metadataText);
        var normalized = RemoveDiacritics(source).ToUpperInvariant();

        if (Regex.IsMatch(normalized, @"\b(?:PUERTO\s+)?CALDERA\b"))
        {
            return "Puerto Caldera";
        }

        if (Regex.IsMatch(normalized, @"\bMOIN\b"))
        {
            return "Moín";
        }

        if (Regex.IsMatch(normalized, @"\b(?:PUERTO\s+)?LIMON\b"))
        {
            return "Puerto Limón";
        }

        if (Regex.IsMatch(normalized, @"\bCOLON\b"))
        {
            return "Colón";
        }

        if (Regex.IsMatch(normalized, @"\bMANZANILLO\b"))
        {
            return "Manzanillo";
        }

        return null;
    }

    private static bool TryParseValidity(
        string metadataText,
        out DateTime validFrom,
        out DateTime validTo
    )
    {
        validFrom = default;
        validTo = default;

        var normalized = RemoveDiacritics(metadataText);
        var match = Regex.Match(
            normalized,
            @"(?ix)(?:validez|vigencia|validity|del)?\s*(?<start>\d{1,2})\s*(?:al|a|[-–—])\s*(?<end>\d{1,2})\s*(?:de|/|-)?\s*(?<month>enero|january|jan|febrero|february|feb|marzo|march|mar|abril|april|apr|mayo|may|junio|june|jun|julio|july|jul|agosto|august|aug|septiembre|setiembre|september|sep|octubre|october|oct|noviembre|november|nov|diciembre|december|dec)(?:\s*(?:de)?\s*(?<year>20\d{2}))?"
        );

        if (
            !match.Success
            || !int.TryParse(match.Groups["start"].Value, out var startDay)
            || !int.TryParse(match.Groups["end"].Value, out var endDay)
            || !MonthNumbers.TryGetValue(
                match.Groups["month"].Value,
                out var month
            )
        )
        {
            return false;
        }

        var year = ResolveValidityYear(match.Groups["year"].Value, month);

        try
        {
            validFrom = new DateTime(year, month, startDay);
            validTo = new DateTime(year, month, endDay);
            return validTo >= validFrom;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static int ResolveValidityYear(string explicitYear, int month)
    {
        if (int.TryParse(explicitYear, out var parsedYear))
        {
            return parsedYear;
        }

        var today = DateTime.UtcNow.Date;
        var year = today.Year;

        if (month <= today.Month - 7)
        {
            year++;
        }
        else if (month >= today.Month + 7)
        {
            year--;
        }

        return year;
    }

    private static string ResolveRouteMode(string metadataText)
    {
        return Regex.IsMatch(
            metadataText,
            @"\bDT\b|DIAMOND\s*TIER",
            RegexOptions.IgnoreCase
        )
            ? "Diamond Tier"
            : "Marítimo";
    }

    private static string BuildRemarks(string routeMode, string portOfExit)
    {
        return routeMode.Equals("Diamond Tier", StringComparison.OrdinalIgnoreCase)
            ? $"Tarifa {routeMode} vía {portOfExit}. Vigencia obtenida del encabezado del XLSX."
            : $"Tarifa marítima vía {portOfExit}. Vigencia obtenida del encabezado del XLSX.";
    }

    private static string? CleanCellText(IXLCell cell)
    {
        var value = cell.GetFormattedString()?.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string RemoveDiacritics(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private static HeaderRow? FindHeaderRow(IXLRange usedRange)
    {
        var firstRowNumber = usedRange.FirstRowUsed().RowNumber();
        var lastRowNumber = Math.Min(
            usedRange.LastRowUsed().RowNumber(),
            firstRowNumber + MaxHeaderScanRows - 1
        );
        HeaderRow? bestHeader = null;

        for (var rowNumber = firstRowNumber; rowNumber <= lastRowNumber; rowNumber++)
        {
            var row = usedRange.Worksheet.Row(rowNumber);
            var cells = row
                .CellsUsed()
                .Select(cell => new HeaderColumn(
                    cell.Address.ColumnNumber,
                    cell.GetString().Trim()
                ))
                .Where(cell => !string.IsNullOrWhiteSpace(cell.Header))
                .ToArray();

            if (cells.Length == 0)
            {
                continue;
            }

            var score = cells.Count(cell =>
            {
                var normalizedHeader = ColumnHeaderNormalizer.Normalize(cell.Header);

                return DefaultFclColumnMappings.Mappings.ContainsKey(normalizedHeader)
                    || IsContainerAmountHeader(cell.Header);
            });

            if (score >= 2)
            {
                return CreateHeaderRow(rowNumber, cells);
            }

            if (bestHeader is null && cells.Length >= 3)
            {
                bestHeader = CreateHeaderRow(rowNumber, cells);
            }
        }

        return bestHeader;
    }

    private static bool IsContainerAmountHeader(string header)
    {
        if (PricingContainerVariants.Expand(header).Count > 0)
        {
            return true;
        }

        var normalizedHeader = ColumnHeaderNormalizer.Normalize(header);

        return Regex.IsMatch(
            normalizedHeader,
            @"^(20|20gp|20dc|20dv|20std|20ft|20dry|40|40gp|40dc|40dv|40std|40ft|40dry|40hc|40hq|40highcube|45hc|45hq)(usd|eur|crc|rate|rates|freight|flete|tarifa|amount|costo|venta|sale|allin|oceanfreight)?$"
        );
    }

    private static HeaderRow CreateHeaderRow(
        int rowNumber,
        IReadOnlyCollection<HeaderColumn> cells
    )
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var columns = new List<HeaderColumn>();

        foreach (var cell in cells)
        {
            var header = cell.Header.Trim();

            if (seen.TryGetValue(header, out var count))
            {
                count++;
                seen[header] = count;
                header = $"{header}_{count}";
            }
            else
            {
                seen[header] = 1;
            }

            columns.Add(new HeaderColumn(cell.ColumnNumber, header));
        }

        return new HeaderRow(
            rowNumber,
            columns,
            columns.Select(x => x.Header).ToArray()
        );
    }

    private sealed record HeaderRow(
        int RowNumber,
        IReadOnlyCollection<HeaderColumn> Columns,
        IReadOnlyCollection<string> Headers
    );

    private sealed record HeaderColumn(int ColumnNumber, string Header);
}
