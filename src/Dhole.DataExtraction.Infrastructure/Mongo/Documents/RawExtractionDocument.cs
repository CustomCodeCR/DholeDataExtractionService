namespace Dhole.DataExtraction.Infrastructure.Mongo.Documents;

public sealed record RawExtractionDocument(
    Guid Id,
    Guid ExtractionExecutionId,
    string OriginalFileName,
    string? RawText,
    string? MetadataJson,
    DateTime CreatedAtUtc
);
