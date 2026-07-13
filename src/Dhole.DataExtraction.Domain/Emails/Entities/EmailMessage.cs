using CustomCodeFramework.Core.Domain.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;

namespace Dhole.DataExtraction.Domain.Emails.Entities;

public sealed class EmailMessage : SoftDeletableAggregateRoot<Guid>
{
    private EmailMessage() { }

    private EmailMessage(
        Guid id,
        Guid emailIngestionAccountId,
        string externalMessageId,
        long? uid,
        string? messageIdHeader,
        string? fromName,
        string fromAddress,
        string? toAddresses,
        string? ccAddresses,
        string subject,
        string? bodyText,
        string? bodyHtml,
        DateTime receivedAt,
        bool hasAttachments,
        string? rawEmailStoragePath,
        string? rawMetadataJson,
        Guid? createdBy
    )
        : base(id)
    {
        EmailIngestionAccountId = emailIngestionAccountId;
        ExternalMessageId = NormalizeRequired(externalMessageId, "El identificador externo del correo es requerido.");
        Uid = uid;
        MessageIdHeader = NormalizeOptional(messageIdHeader);
        FromName = NormalizeOptional(fromName);
        FromAddress = NormalizeRequired(fromAddress, "El remitente del correo es requerido.").ToLowerInvariant();
        ToAddresses = NormalizeOptional(toAddresses);
        CcAddresses = NormalizeOptional(ccAddresses);
        Subject = string.IsNullOrWhiteSpace(subject) ? "(sin asunto)" : subject.Trim();
        BodyText = NormalizeOptional(bodyText);
        BodyHtml = NormalizeOptional(bodyHtml);
        ReceivedAt = receivedAt == default ? DateTime.UtcNow : receivedAt;
        HasAttachments = hasAttachments;
        RawEmailStoragePath = NormalizeOptional(rawEmailStoragePath);
        RawMetadataJson = NormalizeOptional(rawMetadataJson);
        Status = EmailMessageStatus.Received;

        MarkAsCreated(DateTime.UtcNow, createdBy?.ToString());
    }

    public Guid EmailIngestionAccountId { get; private set; }
    public string ExternalMessageId { get; private set; } = string.Empty;
    public long? Uid { get; private set; }
    public string? MessageIdHeader { get; private set; }
    public string? FromName { get; private set; }
    public string FromAddress { get; private set; } = string.Empty;
    public string? ToAddresses { get; private set; }
    public string? CcAddresses { get; private set; }
    public string Subject { get; private set; } = string.Empty;
    public string? BodyText { get; private set; }
    public string? BodyHtml { get; private set; }
    public DateTime ReceivedAt { get; private set; }
    public bool HasAttachments { get; private set; }
    public string? RawEmailStoragePath { get; private set; }
    public string? RawMetadataJson { get; private set; }
    public EmailMessageStatus Status { get; private set; }
    public string? ErrorMessage { get; private set; }
    public decimal? ClassificationConfidence { get; private set; }
    public string? ClassificationReason { get; private set; }
    public DateTime? ProcessedAt { get; private set; }

    public static EmailMessage Create(
        Guid emailIngestionAccountId,
        string externalMessageId,
        long? uid,
        string? messageIdHeader,
        string? fromName,
        string fromAddress,
        string? toAddresses,
        string? ccAddresses,
        string subject,
        string? bodyText,
        string? bodyHtml,
        DateTime receivedAt,
        bool hasAttachments,
        string? rawEmailStoragePath,
        string? rawMetadataJson,
        Guid? createdBy = null
    )
    {
        return Create(
            Guid.NewGuid(),
            emailIngestionAccountId,
            externalMessageId,
            uid,
            messageIdHeader,
            fromName,
            fromAddress,
            toAddresses,
            ccAddresses,
            subject,
            bodyText,
            bodyHtml,
            receivedAt,
            hasAttachments,
            rawEmailStoragePath,
            rawMetadataJson,
            createdBy
        );
    }

    public static EmailMessage Create(
        Guid id,
        Guid emailIngestionAccountId,
        string externalMessageId,
        long? uid,
        string? messageIdHeader,
        string? fromName,
        string fromAddress,
        string? toAddresses,
        string? ccAddresses,
        string subject,
        string? bodyText,
        string? bodyHtml,
        DateTime receivedAt,
        bool hasAttachments,
        string? rawEmailStoragePath,
        string? rawMetadataJson,
        Guid? createdBy = null
    )
    {
        return new EmailMessage(
            id,
            emailIngestionAccountId,
            externalMessageId,
            uid,
            messageIdHeader,
            fromName,
            fromAddress,
            toAddresses,
            ccAddresses,
            subject,
            bodyText,
            bodyHtml,
            receivedAt,
            hasAttachments,
            rawEmailStoragePath,
            rawMetadataJson,
            createdBy
        );
    }

    public void MarkQueued(decimal confidence, string? reason, Guid? updatedBy = null)
    {
        Status = EmailMessageStatus.Queued;
        ClassificationConfidence = Math.Clamp(confidence, 0m, 100m);
        ClassificationReason = NormalizeOptional(reason);
        ErrorMessage = null;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void MarkProcessing(Guid? updatedBy = null)
    {
        Status = EmailMessageStatus.Processing;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void MarkExtracted(Guid? updatedBy = null)
    {
        Status = EmailMessageStatus.Extracted;
        ErrorMessage = null;
        ProcessedAt = DateTime.UtcNow;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void MarkNeedsReview(string? reason, Guid? updatedBy = null)
    {
        Status = EmailMessageStatus.NeedsReview;
        ErrorMessage = NormalizeOptional(reason);
        ProcessedAt = DateTime.UtcNow;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void MarkIgnored(string? reason, Guid? updatedBy = null)
    {
        Status = EmailMessageStatus.Ignored;
        ErrorMessage = NormalizeOptional(reason);
        ProcessedAt = DateTime.UtcNow;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void MarkDuplicated(string? reason, Guid? updatedBy = null)
    {
        Status = EmailMessageStatus.Duplicated;
        ErrorMessage = NormalizeOptional(reason);
        ProcessedAt = DateTime.UtcNow;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void MarkFailed(string errorMessage, Guid? updatedBy = null)
    {
        Status = EmailMessageStatus.Failed;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "Error desconocido al procesar correo." : errorMessage.Trim();
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
