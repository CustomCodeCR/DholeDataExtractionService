namespace Dhole.DataExtraction.Contracts.Extraction;

public sealed record ExtractionDataRequest(
    Guid PricingImportId,
    string CorrelationId,
    string OriginalFileName,
    string? ContentType,
    string? FileExtension,
    long FileSizeBytes,
    string FileHash,
    string? ProfileCode,
    Guid? RequestedBy,
    string? RequestedByName,
    byte[] FileContent
)
{
    public string? SourceOriginType { get; init; }
    public Guid? SourceOriginId { get; init; }
    public Guid? SourceEmailMessageId { get; init; }
    public Guid? SourceEmailAttachmentId { get; init; }
    public string? SourceEmailSubject { get; init; }
    public string? SourceEmailBodyText { get; init; }
    public string? SourceEmailBodyHtml { get; init; }
    /// <summary>
    /// Opaque file reference owned by DholeStorageService. DataExtraction never
    /// creates local paths and does not persist FileContent after processing.
    /// </summary>
    public string? StoragePath { get; init; }
}
