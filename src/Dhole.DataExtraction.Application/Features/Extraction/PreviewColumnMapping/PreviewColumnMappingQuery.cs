using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;

namespace Dhole.DataExtraction.Application.Extraction.PreviewColumnMapping;

public sealed record PreviewColumnMappingQuery(
    string OriginalFileName,
    string? ContentType,
    byte[] FileContent,
    string? ProfileCode
) : IQuery<Result<PreviewColumnMappingResponse>>;

public sealed record PreviewColumnMappingResponse(
    string OriginalFileName,
    string SourceFileType,
    string? ProfileCode,
    IReadOnlyCollection<ColumnMappingPreviewDto> Items
);

public sealed record ColumnMappingPreviewDto(
    string SourceColumnName,
    string NormalizedSourceColumnName,
    string? TargetField,
    bool IsMapped,
    bool IsRequired
);
