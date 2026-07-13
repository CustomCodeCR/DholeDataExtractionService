using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Postgres.EntityFramework.Repositories;
using Dhole.DataExtraction.Application.Abstractions.Repositories.Emails;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Persistence.Repositories.Emails;

public sealed class EmailExtractionJobRepository(ServiceDbContext dbContext)
    : EfRepository<EmailExtractionJob, Guid>(dbContext),
        IEmailExtractionJobRepository
{
    public async Task<IReadOnlyCollection<EmailExtractionJob>> GetPendingAsync(
        int maxItems,
        CancellationToken cancellationToken = default
    )
    {
        var take = maxItems <= 0 ? 25 : maxItems;

        return await dbContext.EmailExtractionJobs
            .Where(x => x.Status == EmailExtractionJobStatus.Pending && !x.IsDeleted)
            .OrderBy(x => x.CreatedAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<EmailExtractionJob>> GetByEmailMessageIdAsync(
        Guid emailMessageId,
        CancellationToken cancellationToken = default
    )
    {
        return await dbContext.EmailExtractionJobs
            .Where(x => x.EmailMessageId == emailMessageId && !x.IsDeleted)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<EmailExtractionJob>> GetPagedAsync(
        PageRequest page,
        EmailExtractionJobStatus? status = null,
        Guid? emailMessageId = null,
        CancellationToken cancellationToken = default
    )
    {
        var query = dbContext.EmailExtractionJobs.AsNoTracking().Where(x => !x.IsDeleted);

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        if (emailMessageId.HasValue)
        {
            query = query.Where(x => x.EmailMessageId == emailMessageId.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<EmailExtractionJob>.Create(items, page.PageNumber, page.PageSize, total);
    }
}
