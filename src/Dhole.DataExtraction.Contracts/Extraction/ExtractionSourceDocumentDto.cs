namespace Dhole.DataExtraction.Contracts.Extraction;

public sealed record ExtractionSourceDocumentDto(
    Guid Id,
    Guid ExtractionExecutionId,
    string OriginalFileName,
    string? ContentType,
    string? FileExtension,
    long FileSizeBytes,
    string FileHash,
    string SourceFileType,
    string? StoragePath
);
