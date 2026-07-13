using Dhole.DataExtraction.Domain.Emails.Entities;

namespace Dhole.DataExtraction.Application.Abstractions.Emails;

public interface IEmailSecretResolver
{
    string ResolvePassword(EmailIngestionAccount account);
}
