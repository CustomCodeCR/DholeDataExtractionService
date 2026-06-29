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
        using var reader = new StreamReader(stream);

        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            DetectDelimiter = true,
            BadDataFound = null,
            MissingFieldFound = null,
            HeaderValidated = null,
            TrimOptions = TrimOptions.Trim,
        };

        using var csv = new CsvReader(reader, configuration);

        await csv.ReadAsync();
        csv.ReadHeader();

        var headers =
            csv.HeaderRecord?.Select(x => x.Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray()
            ?? [];

        var rows = new List<ExtractedRow>();
        var rowNumber = 1;

        while (await csv.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();

            rowNumber++;

            var values = new Dictionary<string, string?>();

            foreach (var header in headers)
            {
                values[header] = csv.GetField(header)?.Trim();
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
}
