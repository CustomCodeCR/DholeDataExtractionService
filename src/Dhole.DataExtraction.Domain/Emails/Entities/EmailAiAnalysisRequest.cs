using CustomCodeFramework.Core.Domain.Entities;

namespace Dhole.DataExtraction.Domain.Emails.Entities;

public sealed class EmailAiAnalysisRequest : AuditableAggregateRoot<Guid>
{
    private EmailAiAnalysisRequest() { }

    private EmailAiAnalysisRequest(
        Guid id,
        Guid emailExtractionJobId,
        Guid emailMessageId,
        Guid? emailAttachmentId,
        Guid? extractionExecutionId,
        Guid provisionalPricingImportId,
        string correlationId,
        string requestHash,
        string payloadJson,
        string? imageStoragePath,
        string? imageContentType
    )
        : base(id)
    {
        EmailExtractionJobId = emailExtractionJobId;
        EmailMessageId = emailMessageId;
        EmailAttachmentId = emailAttachmentId;
        ExtractionExecutionId = extractionExecutionId;
        ProvisionalPricingImportId = provisionalPricingImportId;
        CorrelationId = Required(correlationId, "El CorrelationId es requerido.");
        RequestHash = Required(requestHash, "El hash de la solicitud AI es requerido.");
        PayloadJson = Required(payloadJson, "El payload de la solicitud AI es requerido.");
        ImageStoragePath = Normalize(imageStoragePath);
        ImageContentType = Normalize(imageContentType);

        MarkAsCreated(DateTime.UtcNow, null);
    }

    public Guid EmailExtractionJobId { get; private set; }

    public Guid EmailMessageId { get; private set; }

    public Guid? EmailAttachmentId { get; private set; }

    public Guid? ExtractionExecutionId { get; private set; }

    public Guid ProvisionalPricingImportId { get; private set; }

    public string CorrelationId { get; private set; } = string.Empty;

    public string RequestHash { get; private set; } = string.Empty;

    public string PayloadJson { get; private set; } = string.Empty;

    public string? ImageStoragePath { get; private set; }

    public string? ImageContentType { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public static EmailAiAnalysisRequest Create(
        Guid emailExtractionJobId,
        Guid emailMessageId,
        Guid? emailAttachmentId,
        Guid? extractionExecutionId,
        Guid provisionalPricingImportId,
        string correlationId,
        string requestHash,
        string payloadJson,
        string? imageStoragePath,
        string? imageContentType
    )
    {
        if (
            emailExtractionJobId == Guid.Empty
            || emailMessageId == Guid.Empty
            || provisionalPricingImportId == Guid.Empty
        )
        {
            throw new InvalidOperationException(
                "El trabajo, el correo y el lote provisional son requeridos."
            );
        }

        return new EmailAiAnalysisRequest(
            Guid.NewGuid(),
            emailExtractionJobId,
            emailMessageId,
            emailAttachmentId,
            extractionExecutionId,
            provisionalPricingImportId,
            correlationId,
            requestHash,
            payloadJson,
            imageStoragePath,
            imageContentType
        );
    }

    public void MarkCompleted()
    {
        if (CompletedAtUtc.HasValue)
        {
            return;
        }

        CompletedAtUtc = DateTime.UtcNow;
        MarkAsUpdated(CompletedAtUtc.Value, null);
    }

    private static string Required(string? value, string errorMessage)
    {
        return Normalize(value) ?? throw new InvalidOperationException(errorMessage);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
