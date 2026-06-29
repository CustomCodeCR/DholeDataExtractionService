using Dhole.DataExtraction.Domain.Extraction.Enums;

namespace Dhole.DataExtraction.Application.Abstractions.Extraction;

public interface IDocumentExtractor
{
    SourceFileType FileType { get; }

    Task<ExtractedDocument> ExtractAsync(
        DocumentExtractionInput input,
        CancellationToken cancellationToken = default
    );
}

public sealed record DocumentExtractionInput(
    string OriginalFileName,
    string? ContentType,
    string? FileExtension,
    byte[] FileContent,
    string? ProfileCode = null
);

public sealed record ExtractedDocument(
    string OriginalFileName,
    SourceFileType FileType,
    IReadOnlyCollection<ExtractedTable> Tables,
    string? RawText = null,
    string? MetadataJson = null
);

public sealed record ExtractedTable(
    string? SheetName,
    IReadOnlyCollection<string> Headers,
    IReadOnlyCollection<ExtractedRow> Rows
);

public sealed record ExtractedRow(
    int RowNumber,
    IReadOnlyDictionary<string, string?> Values,
    string? RawJson = null
);
