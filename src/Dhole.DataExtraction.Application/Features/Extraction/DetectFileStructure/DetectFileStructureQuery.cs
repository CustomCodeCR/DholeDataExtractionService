using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;

namespace Dhole.DataExtraction.Application.Extraction.DetectFileStructure;

public sealed record DetectFileStructureQuery(
    string OriginalFileName,
    string? ContentType,
    byte[] FileContent,
    string? ProfileCode
) : IQuery<Result<DetectFileStructureResponse>>;

public sealed record DetectFileStructureResponse(
    string OriginalFileName,
    string? ContentType,
    string? FileExtension,
    long FileSizeBytes,
    string FileHash,
    string SourceFileType,
    IReadOnlyCollection<DetectedTableDto> Tables,
    string? RawText
);

public sealed record DetectedTableDto(
    string? SheetName,
    IReadOnlyCollection<string> Headers,
    int RowCount
);
