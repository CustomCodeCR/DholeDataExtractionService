using CustomCodeFramework.Postgres.EntityFramework.Repositories;
using Dhole.DataExtraction.Application.Abstractions.Repositories.Emails;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Persistence.Repositories.Emails;

public sealed class EmailAttachmentRepository(ServiceDbContext dbContext)
    : EfRepository<EmailAttachment, Guid>(dbContext),
        IEmailAttachmentRepository
{
    public async Task<IReadOnlyCollection<EmailAttachment>> GetByEmailMessageIdAsync(
        Guid emailMessageId,
        CancellationToken cancellationToken = default
    )
    {
        return await dbContext.EmailAttachments
            .Where(x => x.EmailMessageId == emailMessageId && !x.IsDeleted)
            .OrderBy(x => x.FileName)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> ExistsByMessageAndHashAsync(
        Guid emailMessageId,
        string fileHash,
        CancellationToken cancellationToken = default
    )
    {
        var value = fileHash.Trim().ToLowerInvariant();

        return dbContext.EmailAttachments.AnyAsync(
            x => x.EmailMessageId == emailMessageId && x.FileHash == value && !x.IsDeleted,
            cancellationToken
        );
    }
}
