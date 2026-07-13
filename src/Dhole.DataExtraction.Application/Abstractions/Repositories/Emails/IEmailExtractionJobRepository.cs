using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;

namespace Dhole.DataExtraction.Application.Abstractions.Repositories.Emails;

public interface IEmailExtractionJobRepository : IRepository<EmailExtractionJob, Guid>
{
    Task<IReadOnlyCollection<EmailExtractionJob>> GetPendingAsync(
        int maxItems,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyCollection<EmailExtractionJob>> GetByEmailMessageIdAsync(
        Guid emailMessageId,
        CancellationToken cancellationToken = default
    );

    Task<PagedResult<EmailExtractionJob>> GetPagedAsync(
        PageRequest page,
        EmailExtractionJobStatus? status = null,
        Guid? emailMessageId = null,
        CancellationToken cancellationToken = default
    );
}
