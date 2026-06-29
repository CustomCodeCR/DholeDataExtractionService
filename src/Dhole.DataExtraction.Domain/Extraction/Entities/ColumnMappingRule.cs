using CustomCodeFramework.Core.Domain.Entities;

namespace Dhole.DataExtraction.Domain.Extraction.Entities;

public sealed class ColumnMappingRule : SoftDeletableAggregateRoot<Guid>
{
    private ColumnMappingRule() { }

    private ColumnMappingRule(
        Guid id,
        Guid columnMappingProfileId,
        string sourceColumnName,
        string normalizedSourceColumnName,
        string targetField,
        bool isRequired,
        int priority,
        string? defaultValue,
        string? transformExpression,
        string? metadataJson,
        Guid? createdBy
    )
        : base(id)
    {
        ColumnMappingProfileId = columnMappingProfileId;

        SourceColumnName = sourceColumnName.Trim();
        NormalizedSourceColumnName = normalizedSourceColumnName.Trim().ToLowerInvariant();
        TargetField = targetField.Trim();

        IsRequired = isRequired;
        Priority = priority;

        DefaultValue = string.IsNullOrWhiteSpace(defaultValue) ? null : defaultValue.Trim();
        TransformExpression = string.IsNullOrWhiteSpace(transformExpression)
            ? null
            : transformExpression.Trim();

        MetadataJson = string.IsNullOrWhiteSpace(metadataJson) ? null : metadataJson;
        IsActive = true;

        MarkAsCreated(DateTime.UtcNow, createdBy?.ToString());
    }

    public Guid ColumnMappingProfileId { get; private set; }

    public string SourceColumnName { get; private set; } = string.Empty;
    public string NormalizedSourceColumnName { get; private set; } = string.Empty;
    public string TargetField { get; private set; } = string.Empty;

    public bool IsRequired { get; private set; }
    public int Priority { get; private set; }

    public string? DefaultValue { get; private set; }
    public string? TransformExpression { get; private set; }
    public string? MetadataJson { get; private set; }

    public bool IsActive { get; private set; }

    public ColumnMappingProfile ColumnMappingProfile { get; private set; } = default!;

    public static ColumnMappingRule Create(
        Guid columnMappingProfileId,
        string sourceColumnName,
        string normalizedSourceColumnName,
        string targetField,
        bool isRequired,
        int priority,
        string? defaultValue,
        string? transformExpression,
        string? metadataJson,
        Guid? createdBy
    )
    {
        return new ColumnMappingRule(
            Guid.NewGuid(),
            columnMappingProfileId,
            sourceColumnName,
            normalizedSourceColumnName,
            targetField,
            isRequired,
            priority,
            defaultValue,
            transformExpression,
            metadataJson,
            createdBy
        );
    }

    public void Update(
        string sourceColumnName,
        string normalizedSourceColumnName,
        string targetField,
        bool isRequired,
        int priority,
        string? defaultValue,
        string? transformExpression,
        string? metadataJson,
        Guid? updatedBy = null
    )
    {
        SourceColumnName = sourceColumnName.Trim();
        NormalizedSourceColumnName = normalizedSourceColumnName.Trim().ToLowerInvariant();
        TargetField = targetField.Trim();

        IsRequired = isRequired;
        Priority = priority;

        DefaultValue = string.IsNullOrWhiteSpace(defaultValue) ? null : defaultValue.Trim();
        TransformExpression = string.IsNullOrWhiteSpace(transformExpression)
            ? null
            : transformExpression.Trim();

        MetadataJson = string.IsNullOrWhiteSpace(metadataJson) ? null : metadataJson;

        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void SetActive(bool isActive, Guid? updatedBy = null)
    {
        if (IsActive == isActive)
        {
            return;
        }

        IsActive = isActive;

        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void Delete(Guid? deletedBy = null)
    {
        MarkAsDeleted(DateTime.UtcNow, deletedBy?.ToString());
    }
}
