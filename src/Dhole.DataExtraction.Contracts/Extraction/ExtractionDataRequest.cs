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
    public string? StoragePath { get; init; }
}
