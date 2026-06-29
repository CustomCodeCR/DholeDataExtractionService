using CustomCodeFramework.Postgres.EntityFramework.Repositories;
using Dhole.DataExtraction.Application.Abstractions.Repositories;
using Dhole.DataExtraction.Domain.Extraction.Entities;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Persistence.Repositories;

public sealed class ColumnMappingProfileRepository(ServiceDbContext dbContext)
    : EfRepository<ColumnMappingProfile, Guid>(dbContext),
        IColumnMappingProfileRepository
{
    public Task<bool> ExistsByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var value = code.Trim();

        return dbContext.ColumnMappingProfiles.AnyAsync(x => x.Code == value, cancellationToken);
    }

    public Task<ColumnMappingProfile?> GetByCodeAsync(
        string code,
        CancellationToken cancellationToken = default
    )
    {
        var value = code.Trim();

        return dbContext
            .ColumnMappingProfiles.Include(x => x.Rules)
            .FirstOrDefaultAsync(x => x.Code == value && !x.IsDeleted, cancellationToken);
    }

    public Task<ColumnMappingProfile?> GetActiveByCodeAsync(
        string code,
        CancellationToken cancellationToken = default
    )
    {
        var value = code.Trim();

        return dbContext
            .ColumnMappingProfiles.Include(x =>
                x.Rules.Where(rule => !rule.IsDeleted && rule.IsActive)
            )
            .FirstOrDefaultAsync(
                x => x.Code == value && !x.IsDeleted && x.IsActive,
                cancellationToken
            );
    }

    public Task<ColumnMappingProfile?> GetDefaultAsync(
        CancellationToken cancellationToken = default
    )
    {
        return dbContext
            .ColumnMappingProfiles.Include(x =>
                x.Rules.Where(rule => !rule.IsDeleted && rule.IsActive)
            )
            .FirstOrDefaultAsync(
                x => x.Code == "default" && !x.IsDeleted && x.IsActive,
                cancellationToken
            );
    }

    public async Task<IReadOnlyCollection<ColumnMappingProfile>> GetActiveAsync(
        CancellationToken cancellationToken = default
    )
    {
        return await dbContext
            .ColumnMappingProfiles.AsNoTracking()
            .Include(x => x.Rules.Where(rule => !rule.IsDeleted && rule.IsActive))
            .Where(x => !x.IsDeleted && x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }
}
