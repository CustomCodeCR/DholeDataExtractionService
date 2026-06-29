using Dhole.DataExtraction.Domain.Extraction.Enums;

namespace Dhole.DataExtraction.Application.Abstractions.Files;

public interface IExtractionFileReader
{
    Task<ExtractionFileInfo> ReadAsync(
        string originalFileName,
        string? contentType,
        byte[] fileContent,
        CancellationToken cancellationToken = default
    );
}

public sealed record ExtractionFileInfo(
    string OriginalFileName,
    string? ContentType,
    string? FileExtension,
    long FileSizeBytes,
    string FileHash,
    SourceFileType SourceFileType,
    byte[] FileContent
);
