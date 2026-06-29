using System.Text.Json;
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

        var normalizedLines = lines
            .Select(NormalizeLine)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var rawText = string.Join(Environment.NewLine, normalizedLines);
        var tables = TryParsePipeDelimitedTables(normalizedLines);

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

            var line = string.Join(
                " ",
                currentRow
                    .OrderBy(word => word.BoundingBox.Left)
                    .Select(word => word.Text.Trim())
                    .Where(text => !string.IsNullOrWhiteSpace(text))
            );

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

    private static string[] SplitPipeLine(string line)
    {
        return line
            .Split('|')
            .Select(x => x.Trim())
            .ToArray();
    }

    private static bool LooksLikeHeader(IReadOnlyCollection<string> parts)
    {
        var normalizedParts = parts.Select(x => x.Trim().ToUpperInvariant()).ToArray();
        var knownHeaders = new[]
        {
            "POL",
            "POE",
            "POD",
            "EQUIPO",
            "NAVIERA",
            "CARRIER",
            "AGENTE",
            "TOTAL VENTA",
            "TOTAL COSTOS",
            "FLETE MARITIMO (COSTO)",
        };

        return knownHeaders.Count(header => normalizedParts.Contains(header)) >= 3;
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
            || line.StartsWith("Pagina ", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Página ", StringComparison.OrdinalIgnoreCase);
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
