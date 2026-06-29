using CustomCodeFramework.Core.Domain.Entities;
using Dhole.DataExtraction.Domain.Extraction.Enums;

namespace Dhole.DataExtraction.Domain.Extraction.Entities;

public sealed class SourceDocument : SoftDeletableAggregateRoot<Guid>
{
    private SourceDocument() { }

    private SourceDocument(
        Guid id,
        Guid extractionExecutionId,
        string originalFileName,
        string? contentType,
        string? fileExtension,
        long fileSizeBytes,
        string fileHash,
        SourceFileType sourceFileType,
        string? storagePath,
        Guid? createdBy
    )
        : base(id)
    {
        ExtractionExecutionId = extractionExecutionId;
        OriginalFileName = originalFileName.Trim();
        ContentType = string.IsNullOrWhiteSpace(contentType) ? null : contentType.Trim();
        FileExtension = string.IsNullOrWhiteSpace(fileExtension)
            ? null
            : fileExtension.Trim().ToLowerInvariant();

        FileSizeBytes = fileSizeBytes;
        FileHash = fileHash.Trim();
        SourceFileType = sourceFileType;
        StoragePath = string.IsNullOrWhiteSpace(storagePath) ? null : storagePath.Trim();

        MarkAsCreated(DateTime.UtcNow, createdBy?.ToString());
    }

    public Guid ExtractionExecutionId { get; private set; }

    public string OriginalFileName { get; private set; } = string.Empty;
    public string? ContentType { get; private set; }
    public string? FileExtension { get; private set; }
    public long FileSizeBytes { get; private set; }
    public string FileHash { get; private set; } = string.Empty;
    public SourceFileType SourceFileType { get; private set; }
    public string? StoragePath { get; private set; }

    public static SourceDocument Create(
        Guid extractionExecutionId,
        string originalFileName,
        string? contentType,
        string? fileExtension,
        long fileSizeBytes,
        string fileHash,
        SourceFileType sourceFileType,
        string? storagePath,
        Guid? createdBy
    )
    {
        return new SourceDocument(
            Guid.NewGuid(),
            extractionExecutionId,
            originalFileName,
            contentType,
            fileExtension,
            fileSizeBytes,
            fileHash,
            sourceFileType,
            storagePath,
            createdBy
        );
    }
}
