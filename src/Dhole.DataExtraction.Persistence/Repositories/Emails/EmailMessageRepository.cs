using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Postgres.EntityFramework.Repositories;
using Dhole.DataExtraction.Application.Abstractions.Repositories.Emails;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Persistence.Repositories.Emails;

public sealed class EmailMessageRepository(ServiceDbContext dbContext)
    : EfRepository<EmailMessage, Guid>(dbContext),
        IEmailMessageRepository
{
    public Task<EmailMessage?> GetByExternalMessageIdAsync(
        Guid accountId,
        string externalMessageId,
        CancellationToken cancellationToken = default
    )
    {
        var value = externalMessageId.Trim();

        return dbContext.EmailMessages.FirstOrDefaultAsync(
            x => x.EmailIngestionAccountId == accountId && x.ExternalMessageId == value && !x.IsDeleted,
            cancellationToken
        );
    }

    public async Task<PagedResult<EmailMessage>> GetPagedAsync(
        PageRequest page,
        string? search = null,
        EmailMessageStatus? status = null,
        Guid? accountId = null,
        CancellationToken cancellationToken = default
    )
    {
        var query = dbContext.EmailMessages.AsNoTracking().Where(x => !x.IsDeleted);

        if (accountId.HasValue)
        {
            query = query.Where(x => x.EmailIngestionAccountId == accountId.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(x => x.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim().ToLowerInvariant();
            query = query.Where(x =>
                x.Subject.ToLower().Contains(value)
                || x.FromAddress.ToLower().Contains(value)
                || (x.FromName != null && x.FromName.ToLower().Contains(value))
                || x.ExternalMessageId.ToLower().Contains(value)
            );
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.ReceivedAt)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .ToListAsync(cancellationToken);

        return PagedResult<EmailMessage>.Create(items, page.PageNumber, page.PageSize, total);
    }
}
