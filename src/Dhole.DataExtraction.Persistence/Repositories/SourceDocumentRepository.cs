using CustomCodeFramework.Postgres.EntityFramework.Repositories;
using Dhole.DataExtraction.Application.Abstractions.Repositories;
using Dhole.DataExtraction.Domain.Extraction.Entities;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Persistence.Repositories;

public sealed class SourceDocumentRepository(ServiceDbContext dbContext)
    : EfRepository<SourceDocument, Guid>(dbContext),
        ISourceDocumentRepository
{
    public Task<SourceDocument?> GetByExtractionExecutionIdAsync(
        Guid extractionExecutionId,
        CancellationToken cancellationToken = default
    )
    {
        return dbContext.SourceDocuments.FirstOrDefaultAsync(
            x => x.ExtractionExecutionId == extractionExecutionId && !x.IsDeleted,
            cancellationToken
        );
    }

    public Task<SourceDocument?> GetByFileHashAsync(
        string fileHash,
        CancellationToken cancellationToken = default
    )
    {
        var value = fileHash.Trim();

        return dbContext.SourceDocuments.FirstOrDefaultAsync(
            x => x.FileHash == value && !x.IsDeleted,
            cancellationToken
        );
    }

    public Task<bool> ExistsByFileHashAsync(
        string fileHash,
        CancellationToken cancellationToken = default
    )
    {
        var value = fileHash.Trim();

        return dbContext.SourceDocuments.AnyAsync(
            x => x.FileHash == value && !x.IsDeleted,
            cancellationToken
        );
    }
}
