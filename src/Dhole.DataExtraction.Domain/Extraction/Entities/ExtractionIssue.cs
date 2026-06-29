using CustomCodeFramework.Core.Domain.Entities;

namespace Dhole.DataExtraction.Domain.Extraction.Entities;

public sealed class ExtractionIssue : SoftDeletableAggregateRoot<Guid>
{
    private ExtractionIssue() { }

    private ExtractionIssue(
        Guid id,
        Guid extractionExecutionId,
        Guid? pricingExtractionRecordId,
        string code,
        string message,
        bool isBlocking,
        string? sourceSheetName,
        int? sourceRowNumber,
        string? columnName,
        string? rawValue,
        Guid? createdBy
    )
        : base(id)
    {
        ExtractionExecutionId = extractionExecutionId;
        PricingExtractionRecordId = pricingExtractionRecordId;
        Code = code.Trim().ToLowerInvariant();
        Message = message.Trim();
        IsBlocking = isBlocking;
        SourceSheetName = string.IsNullOrWhiteSpace(sourceSheetName) ? null : sourceSheetName.Trim();
        SourceRowNumber = sourceRowNumber;
        ColumnName = string.IsNullOrWhiteSpace(columnName) ? null : columnName.Trim();
        RawValue = string.IsNullOrWhiteSpace(rawValue) ? null : rawValue.Trim();

        MarkAsCreated(DateTime.UtcNow, createdBy?.ToString());
    }

    public Guid ExtractionExecutionId { get; private set; }
    public Guid? PricingExtractionRecordId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public bool IsBlocking { get; private set; }
    public string? SourceSheetName { get; private set; }
    public int? SourceRowNumber { get; private set; }
    public string? ColumnName { get; private set; }
    public string? RawValue { get; private set; }

    public static ExtractionIssue Create(
        Guid extractionExecutionId,
        Guid? pricingExtractionRecordId,
        string code,
        string message,
        bool isBlocking,
        string? sourceSheetName,
        int? sourceRowNumber,
        string? columnName,
        string? rawValue,
        Guid? createdBy
    )
    {
        return new ExtractionIssue(
            Guid.NewGuid(),
            extractionExecutionId,
            pricingExtractionRecordId,
            code,
            message,
            isBlocking,
            sourceSheetName,
            sourceRowNumber,
            columnName,
            rawValue,
            createdBy
        );
    }
}
