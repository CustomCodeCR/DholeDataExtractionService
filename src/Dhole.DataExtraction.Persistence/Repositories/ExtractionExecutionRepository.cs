using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Postgres.EntityFramework.Repositories;
using Dhole.DataExtraction.Application.Abstractions.Repositories;
using Dhole.DataExtraction.Domain.Extraction.Entities;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Persistence.Repositories;

public sealed class ExtractionExecutionRepository(ServiceDbContext dbContext)
    : EfRepository<ExtractionExecution, Guid>(dbContext),
        IExtractionExecutionRepository
{
    public Task<ExtractionExecution?> GetByPricingImportIdAsync(
        Guid pricingImportId,
        CancellationToken cancellationToken = default
    )
    {
        return dbContext.ExtractionExecutions.FirstOrDefaultAsync(
            x => x.PricingImportId == pricingImportId && !x.IsDeleted,
            cancellationToken
        );
    }

    public Task<ExtractionExecution?> GetByCorrelationIdAsync(
        string correlationId,
        CancellationToken cancellationToken = default
    )
    {
        var value = correlationId.Trim();

        return dbContext.ExtractionExecutions.FirstOrDefaultAsync(
            x => x.CorrelationId == value && !x.IsDeleted,
            cancellationToken
        );
    }

    public Task<bool> ExistsByPricingImportIdAsync(
        Guid pricingImportId,
        CancellationToken cancellationToken = default
    )
    {
        return dbContext.ExtractionExecutions.AnyAsync(
            x => x.PricingImportId == pricingImportId && !x.IsDeleted,
            cancellationToken
        );
    }

    public async Task<PagedResult<ExtractionExecution>> GetPagedAsync(
        PageRequest page,
        string? search = null,
        CancellationToken cancellationToken = default
    )
    {
        var query = dbContext.ExtractionExecutions.AsNoTracking().Where(x => !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim().ToLower();

            query = query.Where(x =>
                x.OriginalFileName.ToLower().Contains(value)
                || x.CorrelationId.ToLower().Contains(value)
                || x.FileHash.ToLower().Contains(value)
                || (x.ProfileCode != null && x.ProfileCode.ToLower().Contains(value))
            );
        }

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<ExtractionExecution>.Create(
            items,
            page.PageNumber,
            page.PageSize,
            total
        );
    }
}
