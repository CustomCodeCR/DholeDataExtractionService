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
    public Guid? AiRequestId { get; private set; }
    public Guid? AiExecutionId { get; private set; }
    public string? AiRequestHash { get; private set; }
    public Guid? PricingRequestId { get; private set; }
    public EmailExtractionJobStatus Status { get; private set; }
    public decimal? ConfidenceScore { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? LastErrorCode { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime? NextAttemptAtUtc { get; private set; }
    public string? LeaseOwner { get; private set; }
    public DateTime? LeaseExpiresAtUtc { get; private set; }
    public DateTime? LastHeartbeatAtUtc { get; private set; }
    public int Version { get; private set; } = 1;
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

    public void MarkExtracting(
        string leaseOwner,
        DateTime leaseExpiresAtUtc,
        Guid? updatedBy = null
    )
    {
        if (Status != EmailExtractionJobStatus.Pending)
        {
            throw new InvalidOperationException(
                "Solo un trabajo pendiente puede reclamarse para extracción."
            );
        }

        if (string.IsNullOrWhiteSpace(leaseOwner))
        {
            throw new InvalidOperationException("El propietario del lease es requerido.");
        }

        var now = DateTime.UtcNow;
        Status = EmailExtractionJobStatus.Extracting;
        AttemptCount++;
        StartedAt ??= now;
        NextAttemptAtUtc = null;
        LeaseOwner = leaseOwner.Trim();
        LeaseExpiresAtUtc = leaseExpiresAtUtc > now
            ? leaseExpiresAtUtc
            : now.AddMinutes(5);
        LastHeartbeatAtUtc = now;
        LastErrorCode = null;
        ErrorMessage = null;
        Touch(now, updatedBy);
    }

    public void MarkProcessing(Guid? updatedBy = null)
    {
        MarkExtracting(
            $"legacy-{Environment.ProcessId}",
            DateTime.UtcNow.AddMinutes(10),
            updatedBy
        );
    }

    public void MarkAwaitingAi(
        Guid aiRequestId,
        Guid? extractionExecutionId,
        string requestHash,
        Guid? updatedBy = null
    )
    {
        if (Status != EmailExtractionJobStatus.Extracting)
        {
            throw new InvalidOperationException(
                "La solicitud de AI solo puede publicarse después de la extracción determinística."
            );
        }

        if (aiRequestId == Guid.Empty || string.IsNullOrWhiteSpace(requestHash))
        {
            throw new InvalidOperationException(
                "La solicitud de AI y su hash son obligatorios."
            );
        }

        AiRequestId = aiRequestId;
        AiRequestHash = requestHash.Trim();
        ExtractionExecutionId = extractionExecutionId;
        Status = EmailExtractionJobStatus.AwaitingAi;
        ReleaseLeaseCore();
        Touch(DateTime.UtcNow, updatedBy);
    }

    public void MarkAiProcessing(Guid aiRequestId, Guid? updatedBy = null)
    {
        EnsureAiRequest(aiRequestId);

        if (
            Status
            is EmailExtractionJobStatus.ValidatingAiResult
                or EmailExtractionJobStatus.AwaitingPricing
                or EmailExtractionJobStatus.SentToPricing
                or EmailExtractionJobStatus.NeedsReview
                or EmailExtractionJobStatus.Failed
                or EmailExtractionJobStatus.Ignored
        )
        {
            return;
        }

        if (
            Status
            is not EmailExtractionJobStatus.AwaitingAi
                and not EmailExtractionJobStatus.AiProcessing
        )
        {
            throw new InvalidOperationException(
                "El trabajo no está esperando el procesamiento de AI."
            );
        }

        Status = EmailExtractionJobStatus.AiProcessing;
        Touch(DateTime.UtcNow, updatedBy);
    }

    public void MarkValidatingAiResult(
        Guid aiRequestId,
        Guid aiExecutionId,
        Guid? updatedBy = null
    )
    {
        EnsureAiRequest(aiRequestId);

        if (
            Status
            is EmailExtractionJobStatus.AwaitingPricing
                or EmailExtractionJobStatus.SentToPricing
                or EmailExtractionJobStatus.NeedsReview
        )
        {
            return;
        }

        if (
            Status
            is not EmailExtractionJobStatus.AwaitingAi
                and not EmailExtractionJobStatus.AiProcessing
                and not EmailExtractionJobStatus.ValidatingAiResult
        )
        {
            throw new InvalidOperationException(
                "El trabajo no puede validar un resultado de AI en su estado actual."
            );
        }

        AiExecutionId = aiExecutionId == Guid.Empty ? null : aiExecutionId;
        Status = EmailExtractionJobStatus.ValidatingAiResult;
        Touch(DateTime.UtcNow, updatedBy);
    }

    public void MarkAwaitingPricing(
        Guid pricingRequestId,
        Guid extractionExecutionId,
        decimal confidenceScore,
        Guid? updatedBy = null
    )
    {
        if (Status != EmailExtractionJobStatus.ValidatingAiResult)
        {
            throw new InvalidOperationException(
                "Solo un resultado de AI validado puede enviarse a Pricing."
            );
        }

        if (pricingRequestId == Guid.Empty || extractionExecutionId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "La solicitud de Pricing y la ejecución de extracción son obligatorias."
            );
        }

        PricingRequestId = pricingRequestId;
        ExtractionExecutionId = extractionExecutionId;
        ConfidenceScore = Math.Clamp(confidenceScore, 0m, 100m);
        Status = EmailExtractionJobStatus.AwaitingPricing;
        LastErrorCode = null;
        ErrorMessage = null;
        Touch(DateTime.UtcNow, updatedBy);
    }

    public void MarkAwaitingPricingFromDeterministic(
        Guid pricingRequestId,
        Guid extractionExecutionId,
        decimal confidenceScore,
        Guid? updatedBy = null
    )
    {
        if (Status != EmailExtractionJobStatus.Extracting)
        {
            throw new InvalidOperationException(
                "Solo una extracción determinística activa puede enviarse directamente a Pricing."
            );
        }

        if (pricingRequestId == Guid.Empty || extractionExecutionId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "La solicitud de Pricing y la ejecución de extracción son obligatorias."
            );
        }

        PricingRequestId = pricingRequestId;
        ExtractionExecutionId = extractionExecutionId;
        ConfidenceScore = Math.Clamp(confidenceScore, 0m, 100m);
        Status = EmailExtractionJobStatus.AwaitingPricing;
        LastErrorCode = null;
        ErrorMessage = null;
        ReleaseLeaseCore();
        Touch(DateTime.UtcNow, updatedBy);
    }

    public void MarkAwaitingPricingForManualReview(
        Guid pricingRequestId,
        Guid extractionExecutionId,
        Guid? updatedBy = null
    )
    {
        if (Status != EmailExtractionJobStatus.NeedsReview)
        {
            throw new InvalidOperationException(
                "Solo una extracción pendiente de revisión puede enviarse manualmente a Pricing."
            );
        }

        if (pricingRequestId == Guid.Empty || extractionExecutionId == Guid.Empty)
        {
            throw new InvalidOperationException(
                "La solicitud de Pricing y la ejecución de extracción son obligatorias."
            );
        }

        PricingRequestId = pricingRequestId;
        ExtractionExecutionId = extractionExecutionId;
        Status = EmailExtractionJobStatus.AwaitingPricing;
        LastErrorCode = null;
        ErrorMessage = null;
        FinishedAt = null;
        NextAttemptAtUtc = null;
        ReleaseLeaseCore();
        Touch(DateTime.UtcNow, updatedBy);
    }

    public void MarkNeedsReview(Guid? extractionExecutionId, decimal confidenceScore, string? reason, Guid? updatedBy = null)
    {
        ExtractionExecutionId = extractionExecutionId;
        ConfidenceScore = Math.Clamp(confidenceScore, 0m, 100m);
        Status = EmailExtractionJobStatus.NeedsReview;
        LastErrorCode = null;
        ErrorMessage = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        FinishedAt = DateTime.UtcNow;
        ReleaseLeaseCore();
        Touch(DateTime.UtcNow, updatedBy);
    }

    public void MarkNeedsReview(
        Guid? extractionExecutionId,
        decimal confidenceScore,
        string? reason,
        string? errorCode,
        Guid? updatedBy = null
    )
    {
        MarkNeedsReview(extractionExecutionId, confidenceScore, reason, updatedBy);
        LastErrorCode = Normalize(errorCode);
    }

    public void MarkSentToPricing(Guid? extractionExecutionId, Guid pricingImportBatchId, decimal confidenceScore, Guid? updatedBy = null)
    {
        ExtractionExecutionId = extractionExecutionId;
        PricingImportBatchId = pricingImportBatchId;
        ConfidenceScore = Math.Clamp(confidenceScore, 0m, 100m);
        Status = EmailExtractionJobStatus.SentToPricing;
        LastErrorCode = null;
        ErrorMessage = null;
        FinishedAt = DateTime.UtcNow;
        ReleaseLeaseCore();
        Touch(DateTime.UtcNow, updatedBy);
    }

    public void MarkFailed(Guid? extractionExecutionId, string errorMessage, Guid? updatedBy = null)
    {
        ExtractionExecutionId = extractionExecutionId;
        Status = EmailExtractionJobStatus.Failed;
        LastErrorCode = null;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? "Error desconocido al procesar la extracción del correo." : errorMessage.Trim();
        FinishedAt = DateTime.UtcNow;
        ReleaseLeaseCore();
        Touch(DateTime.UtcNow, updatedBy);
    }

    public void MarkFailed(
        Guid? extractionExecutionId,
        string errorCode,
        string errorMessage,
        Guid? updatedBy = null
    )
    {
        MarkFailed(extractionExecutionId, errorMessage, updatedBy);
        LastErrorCode = Normalize(errorCode);
    }

    public void MarkIgnored(string? reason, Guid? updatedBy = null)
    {
        Status = EmailExtractionJobStatus.Ignored;
        LastErrorCode = null;
        ErrorMessage = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        FinishedAt = DateTime.UtcNow;
        ReleaseLeaseCore();
        Touch(DateTime.UtcNow, updatedBy);
    }

    public void ScheduleRetry(
        string errorCode,
        string errorMessage,
        DateTime nextAttemptAtUtc,
        Guid? updatedBy = null
    )
    {
        if (Status != EmailExtractionJobStatus.Extracting)
        {
            throw new InvalidOperationException(
                "Solo una extracción local puede programarse nuevamente."
            );
        }

        Status = EmailExtractionJobStatus.Pending;
        LastErrorCode = Normalize(errorCode);
        ErrorMessage = Normalize(errorMessage);
        NextAttemptAtUtc = nextAttemptAtUtc > DateTime.UtcNow
            ? nextAttemptAtUtc
            : DateTime.UtcNow;
        ReleaseLeaseCore();
        Touch(DateTime.UtcNow, updatedBy);
    }

    public void RenewLease(
        string leaseOwner,
        DateTime leaseExpiresAtUtc,
        Guid? updatedBy = null
    )
    {
        if (
            Status != EmailExtractionJobStatus.Extracting
            || !string.Equals(LeaseOwner, leaseOwner, StringComparison.Ordinal)
        )
        {
            throw new InvalidOperationException(
                "El lease no pertenece a esta ejecución de extracción."
            );
        }

        LeaseExpiresAtUtc = leaseExpiresAtUtc;
        LastHeartbeatAtUtc = DateTime.UtcNow;
        Touch(DateTime.UtcNow, updatedBy);
    }

    public void ReleaseLease(Guid? updatedBy = null)
    {
        ReleaseLeaseCore();
        Touch(DateTime.UtcNow, updatedBy);
    }

    public void Retry(Guid? updatedBy = null)
    {
        ExtractionExecutionId = null;
        PricingImportBatchId = null;
        AiRequestId = null;
        AiExecutionId = null;
        AiRequestHash = null;
        PricingRequestId = null;
        ConfidenceScore = null;
        Status = EmailExtractionJobStatus.Pending;
        AttemptCount = 0;
        LastErrorCode = null;
        ErrorMessage = null;
        NextAttemptAtUtc = null;
        ReleaseLeaseCore();
        StartedAt = null;
        FinishedAt = null;
        Touch(DateTime.UtcNow, updatedBy);
    }

    private void EnsureAiRequest(Guid aiRequestId)
    {
        if (!AiRequestId.HasValue || AiRequestId.Value != aiRequestId)
        {
            throw new InvalidOperationException(
                "El evento de AI no corresponde a la solicitud activa del trabajo."
            );
        }
    }

    private void ReleaseLeaseCore()
    {
        LeaseOwner = null;
        LeaseExpiresAtUtc = null;
        LastHeartbeatAtUtc = null;
    }

    private void Touch(DateTime now, Guid? updatedBy)
    {
        Version++;
        MarkAsUpdated(now, updatedBy?.ToString());
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
