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
);
