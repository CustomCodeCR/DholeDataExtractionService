using CustomCodeFramework.Core.Domain.Entities;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using Dhole.DataExtraction.Domain.Extraction.Events;

namespace Dhole.DataExtraction.Domain.Extraction.Entities;

public sealed class ExtractionExecution : SoftDeletableAggregateRoot<Guid>
{
    private ExtractionExecution() { }

    private ExtractionExecution(
        Guid id,
        Guid pricingImportId,
        string correlationId,
        string originalFileName,
        string? contentType,
        string? fileExtension,
        long fileSizeBytes,
        string fileHash,
        SourceFileType sourceFileType,
        string? profileCode,
        Guid? requestedBy,
        string? requestedByName
    )
        : base(id)
    {
        PricingImportId = pricingImportId;
        CorrelationId = correlationId.Trim();
        OriginalFileName = originalFileName.Trim();
        ContentType = string.IsNullOrWhiteSpace(contentType) ? null : contentType.Trim();
        FileExtension = string.IsNullOrWhiteSpace(fileExtension)
            ? null
            : fileExtension.Trim().ToLowerInvariant();
        FileSizeBytes = fileSizeBytes;
        FileHash = fileHash.Trim();
        SourceFileType = sourceFileType;
        ProfileCode = string.IsNullOrWhiteSpace(profileCode) ? null : profileCode.Trim();
        RequestedBy = requestedBy;
        RequestedByName = string.IsNullOrWhiteSpace(requestedByName)
            ? null
            : requestedByName.Trim();
        Status = ExtractionStatus.Pending;

        MarkAsCreated(DateTime.UtcNow, requestedBy?.ToString());
    }

    public Guid PricingImportId { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;

    public string OriginalFileName { get; private set; } = string.Empty;
    public string? ContentType { get; private set; }
    public string? FileExtension { get; private set; }
    public long FileSizeBytes { get; private set; }
    public string FileHash { get; private set; } = string.Empty;
    public SourceFileType SourceFileType { get; private set; }
    public string? ProfileCode { get; private set; }

    public ExtractionStatus Status { get; private set; }

    public int TotalRows { get; private set; }
    public int ValidRows { get; private set; }
    public int WarningRows { get; private set; }
    public int InvalidRows { get; private set; }

    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? FailedAt { get; private set; }
    public string? ErrorMessage { get; private set; }

    public Guid? RequestedBy { get; private set; }
    public string? RequestedByName { get; private set; }

    public static ExtractionExecution Create(
        Guid pricingImportId,
        string correlationId,
        string originalFileName,
        string? contentType,
        string? fileExtension,
        long fileSizeBytes,
        string fileHash,
        SourceFileType sourceFileType,
        string? profileCode,
        Guid? requestedBy,
        string? requestedByName
    )
    {
        return Create(
            Guid.NewGuid(),
            pricingImportId,
            correlationId,
            originalFileName,
            contentType,
            fileExtension,
            fileSizeBytes,
            fileHash,
            sourceFileType,
            profileCode,
            requestedBy,
            requestedByName
        );
    }

    public static ExtractionExecution Create(
        Guid id,
        Guid pricingImportId,
        string correlationId,
        string originalFileName,
        string? contentType,
        string? fileExtension,
        long fileSizeBytes,
        string fileHash,
        SourceFileType sourceFileType,
        string? profileCode,
        Guid? requestedBy,
        string? requestedByName
    )
    {
        return new ExtractionExecution(
            id,
            pricingImportId,
            correlationId,
            originalFileName,
            contentType,
            fileExtension,
            fileSizeBytes,
            fileHash,
            sourceFileType,
            profileCode,
            requestedBy,
            requestedByName
        );
    }

    public void Start(Guid? updatedBy = null)
    {
        if (Status != ExtractionStatus.Pending)
        {
            throw new InvalidOperationException("La extracción no puede iniciar en su estado actual.");
        }

        Status = ExtractionStatus.Processing;
        StartedAt = DateTime.UtcNow;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());

        AddDomainEvent(
            new ExtractionExecutionStartedDomainEvent(
                Id,
                PricingImportId,
                CorrelationId,
                OriginalFileName,
                SourceFileType,
                updatedBy
            )
        );
    }

    public void Complete(
        int totalRows,
        int validRows,
        int warningRows,
        int invalidRows,
        Guid? updatedBy = null
    )
    {
        if (Status != ExtractionStatus.Processing && Status != ExtractionStatus.Pending)
        {
            throw new InvalidOperationException("La extracción no puede completarse en su estado actual.");
        }

        TotalRows = totalRows;
        ValidRows = validRows;
        WarningRows = warningRows;
        InvalidRows = invalidRows;
        Status = invalidRows > 0 || warningRows > 0
            ? ExtractionStatus.CompletedWithIssues
            : ExtractionStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        ErrorMessage = null;

        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());

        AddDomainEvent(
            new ExtractionExecutionCompletedDomainEvent(
                Id,
                PricingImportId,
                CorrelationId,
                TotalRows,
                ValidRows,
                WarningRows,
                InvalidRows
            )
        );
    }

    public void Fail(string errorMessage, Guid? updatedBy = null)
    {
        Status = ExtractionStatus.Failed;
        FailedAt = DateTime.UtcNow;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage)
            ? "Error desconocido durante la extracción."
            : errorMessage.Trim();

        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());

        AddDomainEvent(
            new ExtractionExecutionFailedDomainEvent(Id, PricingImportId, CorrelationId, ErrorMessage)
        );
    }
}
