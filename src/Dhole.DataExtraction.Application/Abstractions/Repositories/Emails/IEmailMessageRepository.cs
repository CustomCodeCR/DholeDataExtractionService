using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;

namespace Dhole.DataExtraction.Application.Abstractions.Repositories.Emails;

public interface IEmailMessageRepository : IRepository<EmailMessage, Guid>
{
    Task<EmailMessage?> GetByExternalMessageIdAsync(
        Guid accountId,
        string externalMessageId,
        CancellationToken cancellationToken = default
    );

    Task<PagedResult<EmailMessage>> GetPagedAsync(
        PageRequest page,
        string? search = null,
        EmailMessageStatus? status = null,
        Guid? accountId = null,
        CancellationToken cancellationToken = default
    );
}
