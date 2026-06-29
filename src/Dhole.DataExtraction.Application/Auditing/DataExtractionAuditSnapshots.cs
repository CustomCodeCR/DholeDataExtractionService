using Dhole.DataExtraction.Domain.Extraction.Entities;

namespace Dhole.DataExtraction.Application.Auditing;

public sealed record ExtractionExecutionAuditSnapshot(
    Guid Id,
    Guid PricingImportId,
    string CorrelationId,
    string OriginalFileName,
    string? ContentType,
    string? FileExtension,
    long FileSizeBytes,
    string FileHash,
    string SourceFileType,
    string? ProfileCode,
    string Status,
    int TotalRows,
    int ValidRows,
    int WarningRows,
    int InvalidRows,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    DateTime? FailedAt,
    string? ErrorMessage,
    Guid? RequestedBy,
    string? RequestedByName,
    bool IsDeleted
)
{
    public static ExtractionExecutionAuditSnapshot From(ExtractionExecution execution)
    {
        return new ExtractionExecutionAuditSnapshot(
            execution.Id,
            execution.PricingImportId,
            execution.CorrelationId,
            execution.OriginalFileName,
            execution.ContentType,
            execution.FileExtension,
            execution.FileSizeBytes,
            execution.FileHash,
            execution.SourceFileType.ToString(),
            execution.ProfileCode,
            execution.Status.ToString(),
            execution.TotalRows,
            execution.ValidRows,
            execution.WarningRows,
            execution.InvalidRows,
            execution.StartedAt,
            execution.CompletedAt,
            execution.FailedAt,
            execution.ErrorMessage,
            execution.RequestedBy,
            execution.RequestedByName,
            execution.IsDeleted
        );
    }
}

public sealed record SourceDocumentAuditSnapshot(
    Guid Id,
    Guid ExtractionExecutionId,
    string OriginalFileName,
    string? ContentType,
    string? FileExtension,
    long FileSizeBytes,
    string FileHash,
    string SourceFileType,
    string? StoragePath,
    bool IsDeleted
)
{
    public static SourceDocumentAuditSnapshot From(SourceDocument document)
    {
        return new SourceDocumentAuditSnapshot(
            document.Id,
            document.ExtractionExecutionId,
            document.OriginalFileName,
            document.ContentType,
            document.FileExtension,
            document.FileSizeBytes,
            document.FileHash,
            document.SourceFileType.ToString(),
            document.StoragePath,
            document.IsDeleted
        );
    }
}

public sealed record PricingExtractionRecordAuditSnapshot(
    Guid Id,
    Guid ExtractionExecutionId,
    Guid SourceDocumentId,
    string? SourceSheetName,
    int? SourceRowNumber,
    string? OriginPort,
    string? PortOfExit,
    string? DestinationPort,
    string? ContainerType,
    string? Carrier,
    string? Agent,
    string? Commodity,
    string? Currency,
    DateTime? ValidFrom,
    DateTime? ValidTo,
    decimal? OceanFreight,
    decimal? OriginCharges,
    decimal? DestinationCharges,
    decimal? Surcharges,
    decimal? TotalCost,
    decimal? TotalSale,
    decimal? Profit,
    decimal? Margin,
    string? SpaceComment,
    string? Remarks,
    string Status,
    string? RawJson,
    bool IsDeleted
)
{
    public static PricingExtractionRecordAuditSnapshot From(PricingExtractionRecord record)
    {
        return new PricingExtractionRecordAuditSnapshot(
            record.Id,
            record.ExtractionExecutionId,
            record.SourceDocumentId,
            record.SourceSheetName,
            record.SourceRowNumber,
            record.OriginPort,
            record.PortOfExit,
            record.DestinationPort,
            record.ContainerType,
            record.Carrier,
            record.Agent,
            record.Commodity,
            record.Currency,
            record.ValidFrom,
            record.ValidTo,
            record.OceanFreight,
            record.OriginCharges,
            record.DestinationCharges,
            record.Surcharges,
            record.TotalCost,
            record.TotalSale,
            record.Profit,
            record.Margin,
            record.SpaceComment,
            record.Remarks,
            record.Status.ToString(),
            record.RawJson,
            record.IsDeleted
        );
    }
}

public sealed record ExtractionIssueAuditSnapshot(
    Guid Id,
    Guid ExtractionExecutionId,
    Guid? PricingExtractionRecordId,
    string Code,
    string Message,
    bool IsBlocking,
    string? SourceSheetName,
    int? SourceRowNumber,
    string? ColumnName,
    string? RawValue,
    bool IsDeleted
)
{
    public static ExtractionIssueAuditSnapshot From(ExtractionIssue issue)
    {
        return new ExtractionIssueAuditSnapshot(
            issue.Id,
            issue.ExtractionExecutionId,
            issue.PricingExtractionRecordId,
            issue.Code,
            issue.Message,
            issue.IsBlocking,
            issue.SourceSheetName,
            issue.SourceRowNumber,
            issue.ColumnName,
            issue.RawValue,
            issue.IsDeleted
        );
    }
}

public sealed record ColumnMappingProfileAuditSnapshot(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    bool IsSystem,
    bool IsActive,
    bool IsDeleted,
    IReadOnlyCollection<Guid> RuleIds
)
{
    public static ColumnMappingProfileAuditSnapshot From(ColumnMappingProfile profile)
    {
        return new ColumnMappingProfileAuditSnapshot(
            profile.Id,
            profile.Code,
            profile.Name,
            profile.Description,
            profile.IsSystem,
            profile.IsActive,
            profile.IsDeleted,
            profile.Rules.Select(x => x.Id).OrderBy(x => x).ToArray()
        );
    }
}

public sealed record ColumnMappingRuleAuditSnapshot(
    Guid Id,
    Guid ColumnMappingProfileId,
    string SourceColumnName,
    string NormalizedSourceColumnName,
    string TargetField,
    bool IsRequired,
    int Priority,
    string? DefaultValue,
    string? TransformExpression,
    string? MetadataJson,
    bool IsActive,
    bool IsDeleted
)
{
    public static ColumnMappingRuleAuditSnapshot From(ColumnMappingRule rule)
    {
        return new ColumnMappingRuleAuditSnapshot(
            rule.Id,
            rule.ColumnMappingProfileId,
            rule.SourceColumnName,
            rule.NormalizedSourceColumnName,
            rule.TargetField,
            rule.IsRequired,
            rule.Priority,
            rule.DefaultValue,
            rule.TransformExpression,
            rule.MetadataJson,
            rule.IsActive,
            rule.IsDeleted
        );
    }
}
