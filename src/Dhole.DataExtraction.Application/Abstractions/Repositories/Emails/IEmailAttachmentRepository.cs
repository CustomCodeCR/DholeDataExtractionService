using CustomCodeFramework.Persistence.Abstractions;
using Dhole.DataExtraction.Domain.Emails.Entities;

namespace Dhole.DataExtraction.Application.Abstractions.Repositories.Emails;

public interface IEmailAttachmentRepository : IRepository<EmailAttachment, Guid>
{
    Task<IReadOnlyCollection<EmailAttachment>> GetByEmailMessageIdAsync(
        Guid emailMessageId,
        CancellationToken cancellationToken = default
    );

    Task<bool> ExistsByMessageAndHashAsync(
        Guid emailMessageId,
        string fileHash,
        CancellationToken cancellationToken = default
    );
}
