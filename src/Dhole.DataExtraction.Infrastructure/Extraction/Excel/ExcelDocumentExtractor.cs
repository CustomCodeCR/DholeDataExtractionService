using ClosedXML.Excel;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Enums;

namespace Dhole.DataExtraction.Infrastructure.Extraction.Excel;

public sealed class ExcelDocumentExtractor : IDocumentExtractor
{
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

            var firstRow = usedRange.FirstRowUsed();
            var headerCells = firstRow.CellsUsed().ToArray();

            var headers = headerCells
                .Select(cell => cell.GetString().Trim())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .ToArray();

            if (headers.Length == 0)
            {
                continue;
            }

            var rows = new List<ExtractedRow>();

            foreach (var row in usedRange.RowsUsed().Skip(1))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var values = new Dictionary<string, string?>();

                for (var i = 0; i < headers.Length; i++)
                {
                    var cell = row.Cell(i + 1);
                    values[headers[i]] = cell.GetFormattedString()?.Trim();
                }

                if (values.Values.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                rows.Add(new ExtractedRow(row.RowNumber(), values));
            }

            tables.Add(new ExtractedTable(worksheet.Name, headers, rows));
        }

        var document = new ExtractedDocument(input.OriginalFileName, SourceFileType.Excel, tables);

        return Task.FromResult(document);
    }
}
