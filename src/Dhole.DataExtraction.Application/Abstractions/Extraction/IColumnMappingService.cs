namespace Dhole.DataExtraction.Application.Abstractions.Extraction;

public interface IColumnMappingService
{
    Task<IReadOnlyCollection<MappedPricingRow>> MapAsync(
        ExtractedDocument document,
        string? profileCode = null,
        CancellationToken cancellationToken = default
    );

    Task<ColumnMappingPreviewResult> PreviewAsync(
        ExtractedDocument document,
        string? profileCode = null,
        CancellationToken cancellationToken = default
    );
}

public sealed record MappedPricingRow(
    string? SourceSheetName,
    int SourceRowNumber,
    IReadOnlyDictionary<string, string?> Values,
    string? RawJson = null
);

public sealed record ColumnMappingPreviewResult(
    string? ProfileCode,
    IReadOnlyCollection<ColumnMappingPreviewItem> Items
);

public sealed record ColumnMappingPreviewItem(
    string SourceColumnName,
    string NormalizedSourceColumnName,
    string? TargetField,
    bool IsMapped,
    bool IsRequired
);
