using ClosedXML.Excel;
using System.Text.RegularExpressions;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Infrastructure.Mapping;
using Dhole.DataExtraction.Domain.Extraction.Enums;

namespace Dhole.DataExtraction.Infrastructure.Extraction.Excel;

public sealed class ExcelDocumentExtractor : IDocumentExtractor
{
    private const int MaxHeaderScanRows = 30;

    public SourceFileType FileType => SourceFileType.Excel;

    public Task<ExtractedDocument> ExtractAsync(
        DocumentExtractionInput input,
        CancellationToken cancellationToken = default
    )
    {
        using var stream = new MemoryStream(input.FileContent);
        using var workbook = new XLWorkbook(stream);

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

    private static HeaderRow? FindHeaderRow(IXLRange usedRange)
    {
        var firstRowNumber = usedRange.FirstRowUsed().RowNumber();
        var lastRowNumber = Math.Min(usedRange.LastRowUsed().RowNumber(), firstRowNumber + MaxHeaderScanRows - 1);
        HeaderRow? bestHeader = null;

        for (var rowNumber = firstRowNumber; rowNumber <= lastRowNumber; rowNumber++)
        {
            var row = usedRange.Worksheet.Row(rowNumber);
            var cells = row
                .CellsUsed()
                .Select(cell => new HeaderColumn(cell.Address.ColumnNumber, cell.GetString().Trim()))
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
                    || IsContainerAmountHeader(normalizedHeader);
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

    private static bool IsContainerAmountHeader(string normalizedHeader)
    {
        return Regex.IsMatch(
            normalizedHeader,
            @"^(20|20gp|20dc|20dv|20std|20ft|20dry|40|40gp|40dc|40dv|40std|40ft|40dry|40hc|40hq|40highcube|45hc|45hq)(usd|eur|crc|rate|rates|freight|flete|tarifa|amount|costo|venta|sale|allin|oceanfreight)?$"
        );
    }

    private static HeaderRow CreateHeaderRow(int rowNumber, IReadOnlyCollection<HeaderColumn> cells)
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

        return new HeaderRow(rowNumber, columns, columns.Select(x => x.Header).ToArray());
    }

    private sealed record HeaderRow(
        int RowNumber,
        IReadOnlyCollection<HeaderColumn> Columns,
        IReadOnlyCollection<string> Headers
    );

    private sealed record HeaderColumn(int ColumnNumber, string Header);
}
