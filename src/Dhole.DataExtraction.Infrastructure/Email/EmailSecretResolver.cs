using Dhole.DataExtraction.Application.Abstractions.Emails;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Microsoft.Extensions.Configuration;

namespace Dhole.DataExtraction.Infrastructure.Email;

public sealed class EmailSecretResolver(IConfiguration configuration) : IEmailSecretResolver
{
    public string ResolvePassword(EmailIngestionAccount account)
    {
        var reference = account.SecretReference.Trim();
        var configuredValue = configuration[reference];

        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            return NormalizePassword(configuredValue, account.ProviderType);
        }

        var environmentValue =
            Environment.GetEnvironmentVariable(reference)
            ?? Environment.GetEnvironmentVariable(reference.Replace(":", "__", StringComparison.Ordinal))
            ?? Environment.GetEnvironmentVariable(reference.Replace(':', '_'));

        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return NormalizePassword(environmentValue, account.ProviderType);
        }

        throw new InvalidOperationException(
            $"No se encontró el secreto de la cuenta {account.EmailAddress}. "
                + $"Configure la variable de entorno '{reference}'."
        );
    }

    private static string NormalizePassword(string value, EmailProviderType providerType)
    {
        var password = value.Trim();

        if (providerType == EmailProviderType.Gmail)
        {
            password = password.Replace(" ", string.Empty);
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("La contraseña del correo no está configurada.");
        }

        return password;
    }
}
