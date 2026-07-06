using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Enums;

namespace Dhole.DataExtraction.Infrastructure.Extraction.Csv;

public sealed class CsvDocumentExtractor : IDocumentExtractor
{
    public SourceFileType FileType => SourceFileType.Csv;

    public async Task<ExtractedDocument> ExtractAsync(
        DocumentExtractionInput input,
        CancellationToken cancellationToken = default
    )
    {
        using var stream = new MemoryStream(input.FileContent);
        using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);

        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            DetectDelimiter = true,
            BadDataFound = null,
            MissingFieldFound = null,
            HeaderValidated = null,
            TrimOptions = TrimOptions.Trim,
            IgnoreBlankLines = true,
        };

        using var csv = new CsvReader(reader, configuration);

        if (!await csv.ReadAsync())
        {
            return new ExtractedDocument(input.OriginalFileName, SourceFileType.Csv, []);
        }

        csv.ReadHeader();

        var headers = NormalizeHeaders(csv.HeaderRecord ?? []);

        if (headers.Length == 0)
        {
            return new ExtractedDocument(input.OriginalFileName, SourceFileType.Csv, []);
        }

        var rows = new List<ExtractedRow>();
        var rowNumber = 1;

        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            rowNumber++;

            var values = new Dictionary<string, string?>();

            for (var i = 0; i < headers.Length; i++)
            {
                var field = csv.TryGetField(i, out string? value) ? value?.Trim() : null;
                values[headers[i]] = string.IsNullOrWhiteSpace(field) ? null : field;
            }

            if (values.Values.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            rows.Add(new ExtractedRow(rowNumber, values));
        }

        var table = new ExtractedTable("CSV", headers, rows);

        return new ExtractedDocument(input.OriginalFileName, SourceFileType.Csv, [table]);
    }

    private static string[] NormalizeHeaders(IReadOnlyCollection<string> rawHeaders)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var headers = new List<string>();

        foreach (var rawHeader in rawHeaders)
        {
            var header = rawHeader?.Trim();

            if (string.IsNullOrWhiteSpace(header))
            {
                continue;
            }

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

            headers.Add(header);
        }

        return headers.ToArray();
    }
}
