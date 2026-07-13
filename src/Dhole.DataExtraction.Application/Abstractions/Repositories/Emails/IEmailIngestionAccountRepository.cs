using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.DataExtraction.Domain.Emails.Entities;

namespace Dhole.DataExtraction.Application.Abstractions.Repositories.Emails;

public interface IEmailIngestionAccountRepository : IRepository<EmailIngestionAccount, Guid>
{
    Task<IReadOnlyCollection<EmailIngestionAccount>> GetActiveAsync(CancellationToken cancellationToken = default);

    Task<PagedResult<EmailIngestionAccount>> GetPagedAsync(
        PageRequest page,
        string? search = null,
        bool? isActive = null,
        CancellationToken cancellationToken = default
    );

    Task<bool> ExistsByEmailAddressAsync(string emailAddress, Guid? excludeId = null, CancellationToken cancellationToken = default);
}
