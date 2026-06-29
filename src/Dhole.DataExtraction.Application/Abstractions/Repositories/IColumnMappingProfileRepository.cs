using CustomCodeFramework.Persistence.Abstractions;
using Dhole.DataExtraction.Domain.Extraction.Entities;

namespace Dhole.DataExtraction.Application.Abstractions.Repositories;

public interface IColumnMappingProfileRepository : IRepository<ColumnMappingProfile, Guid>
{
    Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<ColumnMappingProfile?> GetByCodeAsync(
        string code,
        CancellationToken cancellationToken = default
    );

    Task<ColumnMappingProfile?> GetActiveByCodeAsync(
        string code,
        CancellationToken cancellationToken = default
    );

    Task<ColumnMappingProfile?> GetDefaultAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ColumnMappingProfile>> GetActiveAsync(
        CancellationToken cancellationToken = default
    );
}
