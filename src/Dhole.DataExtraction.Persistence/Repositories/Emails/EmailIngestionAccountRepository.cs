using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Postgres.EntityFramework.Repositories;
using Dhole.DataExtraction.Application.Abstractions.Repositories.Emails;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Persistence.Repositories.Emails;

public sealed class EmailIngestionAccountRepository(ServiceDbContext dbContext)
    : EfRepository<EmailIngestionAccount, Guid>(dbContext),
        IEmailIngestionAccountRepository
{
    public async Task<IReadOnlyCollection<EmailIngestionAccount>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.EmailIngestionAccounts
            .Where(x => x.IsActive && !x.IsDeleted)
            .OrderBy(x => x.EmailAddress)
            .ToListAsync(cancellationToken);
    }

    public async Task<PagedResult<EmailIngestionAccount>> GetPagedAsync(
        PageRequest page,
        string? search = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default
    )
    {
        var query = dbContext.EmailIngestionAccounts.AsNoTracking().Where(x => !x.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.Name.ToLower().Contains(value)
                || x.EmailAddress.ToLower().Contains(value)
                || x.Username.ToLower().Contains(value)
                || x.Host.ToLower().Contains(value)
            );
        }

        if (isActive.HasValue)
        {
            query = query.Where(x => x.IsActive == isActive.Value);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<EmailIngestionAccount>.Create(items, page.PageNumber, page.PageSize, total);
    }

    public Task<bool> ExistsByEmailAddressAsync(string emailAddress, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var value = emailAddress.Trim().ToLowerInvariant();

        return dbContext.EmailIngestionAccounts.AnyAsync(
            x => x.EmailAddress == value && !x.IsDeleted && (!excludeId.HasValue || x.Id != excludeId.Value),
            cancellationToken
        );
    }
}
