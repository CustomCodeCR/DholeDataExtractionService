using CustomCodeFramework.Core.Domain.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;

namespace Dhole.DataExtraction.Domain.Emails.Entities;

public sealed class EmailExtractionJob : SoftDeletableAggregateRoot<Guid>
{
    private EmailExtractionJob() { }

    private EmailExtractionJob(
        Guid id,
        Guid emailMessageId,
        Guid? emailAttachmentId,
        EmailContentSourceType sourceType,
        Guid provisionalPricingImportId,
        Guid? createdBy
    )
        : base(id)
    {
        EmailMessageId = emailMessageId;
        EmailAttachmentId = emailAttachmentId;
        SourceType = sourceType;
        ProvisionalPricingImportId = provisionalPricingImportId;
        Status = EmailExtractionJobStatus.Pending;

        MarkAsCreated(DateTime.UtcNow, createdBy?.ToString());
    }

    public Guid EmailMessageId { get; private set; }
    public Guid? EmailAttachmentId { get; private set; }
    public EmailContentSourceType SourceType { get; private set; }
    public Guid ProvisionalPricingImportId { get; private set; }
    public Guid? ExtractionExecutionId { get; private set; }
    public Guid? PricingImportBatchId { get; private set; }
    public EmailExtractionJobStatus Status { get; private set; }
    public decimal? ConfidenceScore { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? FinishedAt { get; private set; }

    public static EmailExtractionJob CreateBodyJob(Guid emailMessageId, Guid? createdBy = null)
    {
        return new EmailExtractionJob(
            Guid.NewGuid(),
            emailMessageId,
            null,
            EmailContentSourceType.Body,
            Guid.NewGuid(),
            createdBy
        );
    }

    public static EmailExtractionJob CreateAttachmentJob(Guid emailMessageId, Guid emailAttachmentId, Guid? createdBy = null)
    {
        return new EmailExtractionJob(
            Guid.NewGuid(),
            emailMessageId,
            emailAttachmentId,
            EmailContentSourceType.Attachment,
            Guid.NewGuid(),
            createdBy
        );
    }

    public void MarkProcessing(Guid? updatedBy = null)
    {
        Status = EmailExtractionJobStatus.Processing;
        StartedAt ??= DateTime.UtcNow;
        ErrorMessage = null;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void MarkNeedsReview(Guid? extractionExecutionId, decimal confidenceScore, string? reason, Guid? updatedBy = null)
    {
        ExtractionExecutionId = extractionExecutionId;
        ConfidenceScore = Math.Clamp(confidenceScore, 0m, 100m);
        Status = EmailExtractionJobStatus.NeedsReview;
        ErrorMessage = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        FinishedAt = DateTime.UtcNow;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void MarkSentToPricing(Guid? extractionExecutionId, Guid pricingImportBatchId, decimal confidenceScore, Guid? updatedBy = null)
    {
        ExtractionExecutionId = extractionExecutionId;
        PricingImportBatchId = pricingImportBatchId;
        ConfidenceScore = Math.Clamp(confidenceScore, 0m, 100m);
        Status = EmailExtractionJobStatus.SentToPricing;
        ErrorMessage = null;
        FinishedAt = DateTime.UtcNow;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void MarkFailed(Guid? extractionExecutionId, string errorMessage, Guid? updatedBy = null)
    {
        ExtractionExecutionId = extractionExecutionId;
        Status = EmailExtractionJobStatus.Failed;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "Error desconocido al procesar la extracción del correo." : errorMessage.Trim();
        FinishedAt = DateTime.UtcNow;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void MarkIgnored(string? reason, Guid? updatedBy = null)
    {
        Status = EmailExtractionJobStatus.Ignored;
        ErrorMessage = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        FinishedAt = DateTime.UtcNow;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void Retry(Guid? updatedBy = null)
    {
        ExtractionExecutionId = null;
        PricingImportBatchId = null;
        ConfidenceScore = null;
        Status = EmailExtractionJobStatus.Pending;
        ErrorMessage = null;
        StartedAt = null;
        FinishedAt = null;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }
}
