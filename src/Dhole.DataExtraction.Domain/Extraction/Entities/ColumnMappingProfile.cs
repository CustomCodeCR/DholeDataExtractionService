using CustomCodeFramework.Core.Domain.Entities;

namespace Dhole.DataExtraction.Domain.Extraction.Entities;

public sealed class ColumnMappingProfile : SoftDeletableAggregateRoot<Guid>
{
    private readonly List<ColumnMappingRule> _rules = [];

    private ColumnMappingProfile() { }

    private ColumnMappingProfile(
        Guid id,
        string code,
        string name,
        string? description,
        bool isSystem,
        Guid? createdBy
    )
        : base(id)
    {
        Code = code.Trim().ToLowerInvariant();
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        IsSystem = isSystem;
        IsActive = true;

        MarkAsCreated(DateTime.UtcNow, createdBy?.ToString());
    }

    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public bool IsSystem { get; private set; }
    public bool IsActive { get; private set; }
    public IReadOnlyCollection<ColumnMappingRule> Rules => _rules;

    public static ColumnMappingProfile Create(
        string code,
        string name,
        string? description,
        bool isSystem,
        Guid? createdBy
    )
    {
        return Create(Guid.NewGuid(), code, name, description, isSystem, createdBy);
    }

    public static ColumnMappingProfile Create(
        Guid id,
        string code,
        string name,
        string? description,
        bool isSystem,
        Guid? createdBy
    )
    {
        return new ColumnMappingProfile(id, code, name, description, isSystem, createdBy);
    }

    public void Update(string name, string? description, Guid? updatedBy = null)
    {
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
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
        if (IsSystem)
        {
            throw new InvalidOperationException("No se puede eliminar un perfil de mapeo del sistema.");
        }

        MarkAsDeleted(DateTime.UtcNow, deletedBy?.ToString());
    }
}
