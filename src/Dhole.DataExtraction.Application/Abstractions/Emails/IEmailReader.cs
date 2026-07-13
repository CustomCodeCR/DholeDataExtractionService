using Dhole.DataExtraction.Domain.Emails.Entities;

namespace Dhole.DataExtraction.Application.Abstractions.Emails;

public interface IEmailReader
{
    Task<IReadOnlyCollection<EmailMessageReadModel>> ReadNewMessagesAsync(
        EmailIngestionAccount account,
        string passwordOrAppPassword,
        int maxMessages,
        CancellationToken cancellationToken = default
    );
}
