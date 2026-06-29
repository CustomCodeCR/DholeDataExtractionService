using CustomCodeFramework.Postgres.EntityFramework.Repositories;
using Dhole.DataExtraction.Application.Abstractions.Repositories;
using Dhole.DataExtraction.Domain.Extraction.Entities;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Persistence.Repositories;

public sealed class ExtractionIssueRepository(ServiceDbContext dbContext)
    : EfRepository<ExtractionIssue, Guid>(dbContext),
        IExtractionIssueRepository
{
    public async Task AddRangeAsync(
        IReadOnlyCollection<ExtractionIssue> issues,
        CancellationToken cancellationToken = default
    )
    {
        await dbContext.ExtractionIssues.AddRangeAsync(issues, cancellationToken);
    }

    public async Task<IReadOnlyCollection<ExtractionIssue>> GetByExtractionExecutionIdAsync(
        Guid extractionExecutionId,
        CancellationToken cancellationToken = default
    )
    {
        return await dbContext
            .ExtractionIssues.AsNoTracking()
            .Where(x => x.ExtractionExecutionId == extractionExecutionId && !x.IsDeleted)
            .OrderBy(x => x.SourceSheetName)
            .ThenBy(x => x.SourceRowNumber)
            .ToListAsync(cancellationToken);
    }
}
