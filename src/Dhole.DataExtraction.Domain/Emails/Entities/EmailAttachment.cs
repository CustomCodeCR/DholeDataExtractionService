using CustomCodeFramework.Core.Domain.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Domain.Extraction.Enums;

namespace Dhole.DataExtraction.Domain.Emails.Entities;

public sealed class EmailAttachment : SoftDeletableAggregateRoot<Guid>
{
    private EmailAttachment() { }

    private EmailAttachment(
        Guid id,
        Guid emailMessageId,
        string fileName,
        string? contentType,
        string? fileExtension,
        long sizeBytes,
        string fileHash,
        string storagePath,
        SourceFileType sourceFileType,
        EmailAttachmentStatus status,
        Guid? createdBy
    )
        : base(id)
    {
        EmailMessageId = emailMessageId;
        FileName = NormalizeRequired(fileName, "El nombre del adjunto es requerido.");
        ContentType = NormalizeOptional(contentType);
        FileExtension = string.IsNullOrWhiteSpace(fileExtension) ? null : fileExtension.Trim().ToLowerInvariant();
        SizeBytes = sizeBytes;
        FileHash = NormalizeRequired(fileHash, "El hash del adjunto es requerido.");
        StoragePath = NormalizeRequired(storagePath, "La ruta de almacenamiento del adjunto es requerida.");
        SourceFileType = sourceFileType;
        Status = status;

        MarkAsCreated(DateTime.UtcNow, createdBy?.ToString());
    }

    public Guid EmailMessageId { get; private set; }
    public string FileName { get; private set; } = string.Empty;
    public string? ContentType { get; private set; }
    public string? FileExtension { get; private set; }
    public long SizeBytes { get; private set; }
    public string FileHash { get; private set; } = string.Empty;
    public string StoragePath { get; private set; } = string.Empty;
    public SourceFileType SourceFileType { get; private set; }
    public EmailAttachmentStatus Status { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTime? ProcessedAt { get; private set; }

    public static EmailAttachment Create(
        Guid emailMessageId,
        string fileName,
        string? contentType,
        string? fileExtension,
        long sizeBytes,
        string fileHash,
        string storagePath,
        SourceFileType sourceFileType,
        Guid? createdBy = null
    )
    {
        return Create(
            Guid.NewGuid(),
            emailMessageId,
            fileName,
            contentType,
            fileExtension,
            sizeBytes,
            fileHash,
            storagePath,
            sourceFileType,
            createdBy
        );
    }

    public static EmailAttachment Create(
        Guid id,
        Guid emailMessageId,
        string fileName,
        string? contentType,
        string? fileExtension,
        long sizeBytes,
        string fileHash,
        string storagePath,
        SourceFileType sourceFileType,
        Guid? createdBy = null
    )
    {
        var status = sourceFileType == SourceFileType.Unknown
            ? EmailAttachmentStatus.Unsupported
            : EmailAttachmentStatus.Supported;

        return new EmailAttachment(
            id,
            emailMessageId,
            fileName,
            contentType,
            fileExtension,
            sizeBytes,
            fileHash,
            storagePath,
            sourceFileType,
            status,
            createdBy
        );
    }

    public void MarkExtracted(Guid? updatedBy = null)
    {
        Status = EmailAttachmentStatus.Extracted;
        ErrorMessage = null;
        ProcessedAt = DateTime.UtcNow;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void MarkDuplicated(string? reason, Guid? updatedBy = null)
    {
        Status = EmailAttachmentStatus.Duplicated;
        ErrorMessage = NormalizeOptional(reason);
        ProcessedAt = DateTime.UtcNow;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void MarkFailed(string errorMessage, Guid? updatedBy = null)
    {
        Status = EmailAttachmentStatus.Failed;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "Error desconocido al procesar adjunto." : errorMessage.Trim();
        ProcessedAt = DateTime.UtcNow;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    private static string NormalizeRequired(string value, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return value.Trim();
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
