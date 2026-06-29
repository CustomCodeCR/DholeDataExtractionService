using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using UglyToad.PdfPig;

namespace Dhole.DataExtraction.Infrastructure.Extraction.Pdf;

public sealed class PdfDocumentExtractor : IDocumentExtractor
{
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

            var text = page.Text;

            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            lines.AddRange(
                text.Split(
                    ['\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
            );
        }

        var rawText = string.Join(Environment.NewLine, lines);

        var table = new ExtractedTable("PDF", [], []);

        return Task.FromResult(
            new ExtractedDocument(input.OriginalFileName, SourceFileType.Pdf, [table], rawText)
        );
    }
}
