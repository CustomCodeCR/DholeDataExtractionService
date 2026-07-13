using Dhole.DataExtraction.Contracts.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Enums;

namespace Dhole.DataExtraction.Application.Abstractions.Emails;

public sealed record EmailAttachmentReadModel(
    string FileName,
    string? ContentType,
    byte[] Content
)
{
    public string FileExtension => Path.GetExtension(FileName).ToLowerInvariant();
}

public sealed record EmailMessageReadModel(
    string ExternalMessageId,
    long? Uid,
    string? MessageIdHeader,
    string? FromName,
    string FromAddress,
    string? ToAddresses,
    string? CcAddresses,
    string Subject,
    string? BodyText,
    string? BodyHtml,
    DateTime ReceivedAt,
    byte[] RawContent,
    IReadOnlyCollection<EmailAttachmentReadModel> Attachments
);

public sealed record EmailClassificationResult(
    bool ContainsRates,
    bool ProcessBody,
    IReadOnlyCollection<Guid> AttachmentIdsToProcess,
    decimal ConfidenceScore,
    string Reason
);

public sealed record PricingImportSubmissionRequest(
    Guid ExtractionExecutionId,
    Guid PricingImportId,
    Guid EmailMessageId,
    Guid? EmailAttachmentId,
    string SourceType,
    string FromAddress,
    string Subject,
    string OriginalFileName,
    decimal ConfidenceScore,
    ExtractPricingDataResponse Response
)
{
    // Alias explícitos para mantener compatibilidad con el contrato HTTP de Pricing.
    public string SourceId => EmailAttachmentId?.ToString() ?? EmailMessageId.ToString();
    public string SourceName => OriginalFileName;
    public string ReceivedFrom => FromAddress;
    public IReadOnlyCollection<ExtractedPricingRowDto> Items => Response.Rows;
    public string? ContentSourceType { get; init; }
}

public sealed record PricingImportSubmissionResult(
    bool Success,
    Guid? PricingImportBatchId,
    string? ErrorMessage
);
