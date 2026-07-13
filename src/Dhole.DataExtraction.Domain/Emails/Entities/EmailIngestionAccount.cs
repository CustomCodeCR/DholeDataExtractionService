using CustomCodeFramework.Core.Domain.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;

namespace Dhole.DataExtraction.Domain.Emails.Entities;

public sealed class EmailIngestionAccount : SoftDeletableAggregateRoot<Guid>
{
    private EmailIngestionAccount() { }

    private EmailIngestionAccount(
        Guid id,
        string name,
        string emailAddress,
        EmailProviderType providerType,
        string? host,
        int port,
        bool useSsl,
        string username,
        string secretReference,
        string folderName,
        int pollingIntervalMinutes,
        bool autoProcess,
        bool autoSendToPricing,
        decimal autoSendMinConfidence,
        bool processBodyWhenNoSupportedAttachments,
        bool processBodyEvenWithAttachments,
        string? allowedSenders,
        Guid? createdBy
    )
        : base(id)
    {
        Name = NormalizeRequired(name, "El nombre de la cuenta de correo es requerido.");
        EmailAddress = NormalizeRequired(emailAddress, "El correo electrónico es requerido.").ToLowerInvariant();
        ProviderType = providerType;
        Host = NormalizeHost(providerType, host);
        Port = port <= 0 ? DefaultPort(providerType) : port;
        UseSsl = useSsl;
        Username = NormalizeRequired(username, "El usuario de correo es requerido.");
        SecretReference = NormalizeSecretReference(secretReference, providerType);
        FolderName = string.IsNullOrWhiteSpace(folderName) ? "INBOX" : folderName.Trim();
        PollingIntervalMinutes = pollingIntervalMinutes <= 0 ? 5 : pollingIntervalMinutes;
        AutoProcess = autoProcess;
        AutoSendToPricing = autoSendToPricing;
        AutoSendMinConfidence = NormalizeConfidence(autoSendMinConfidence);
        ProcessBodyWhenNoSupportedAttachments = processBodyWhenNoSupportedAttachments;
        ProcessBodyEvenWithAttachments = processBodyEvenWithAttachments;
        AllowedSenders = string.IsNullOrWhiteSpace(allowedSenders) ? null : allowedSenders.Trim();
        IsActive = true;

        MarkAsCreated(DateTime.UtcNow, createdBy?.ToString());
    }

    public string Name { get; private set; } = string.Empty;
    public string EmailAddress { get; private set; } = string.Empty;
    public EmailProviderType ProviderType { get; private set; }
    public string Host { get; private set; } = string.Empty;
    public int Port { get; private set; }
    public bool UseSsl { get; private set; }
    public string Username { get; private set; } = string.Empty;
    public string SecretReference { get; private set; } = string.Empty;
    public string FolderName { get; private set; } = "INBOX";
    public int PollingIntervalMinutes { get; private set; }
    public bool AutoProcess { get; private set; }
    public bool AutoSendToPricing { get; private set; }
    public decimal AutoSendMinConfidence { get; private set; }
    public bool ProcessBodyWhenNoSupportedAttachments { get; private set; }
    public bool ProcessBodyEvenWithAttachments { get; private set; }
    public string? AllowedSenders { get; private set; }
    public bool IsActive { get; private set; }
    public long? LastProcessedUid { get; private set; }
    public DateTime? LastSyncAt { get; private set; }
    public string? LastSyncError { get; private set; }

    public static EmailIngestionAccount Create(
        string name,
        string emailAddress,
        EmailProviderType providerType,
        string? host,
        int port,
        bool useSsl,
        string username,
        string secretReference,
        string folderName,
        int pollingIntervalMinutes,
        bool autoProcess,
        bool autoSendToPricing,
        decimal autoSendMinConfidence,
        bool processBodyWhenNoSupportedAttachments,
        bool processBodyEvenWithAttachments,
        string? allowedSenders,
        Guid? createdBy
    )
    {
        return new EmailIngestionAccount(
            Guid.NewGuid(),
            name,
            emailAddress,
            providerType,
            host,
            port,
            useSsl,
            username,
            secretReference,
            folderName,
            pollingIntervalMinutes,
            autoProcess,
            autoSendToPricing,
            autoSendMinConfidence,
            processBodyWhenNoSupportedAttachments,
            processBodyEvenWithAttachments,
            allowedSenders,
            createdBy
        );
    }

    public void Update(
        string name,
        string emailAddress,
        EmailProviderType providerType,
        string? host,
        int port,
        bool useSsl,
        string username,
        string secretReference,
        string folderName,
        int pollingIntervalMinutes,
        bool autoProcess,
        bool autoSendToPricing,
        decimal autoSendMinConfidence,
        bool processBodyWhenNoSupportedAttachments,
        bool processBodyEvenWithAttachments,
        string? allowedSenders,
        Guid? updatedBy
    )
    {
        Name = NormalizeRequired(name, "El nombre de la cuenta de correo es requerido.");
        EmailAddress = NormalizeRequired(emailAddress, "El correo electrónico es requerido.").ToLowerInvariant();
        ProviderType = providerType;
        Host = NormalizeHost(providerType, host);
        Port = port <= 0 ? DefaultPort(providerType) : port;
        UseSsl = useSsl;
        Username = NormalizeRequired(username, "El usuario de correo es requerido.");
        SecretReference = NormalizeSecretReference(secretReference, providerType);
        FolderName = string.IsNullOrWhiteSpace(folderName) ? "INBOX" : folderName.Trim();
        PollingIntervalMinutes = pollingIntervalMinutes <= 0 ? 5 : pollingIntervalMinutes;
        AutoProcess = autoProcess;
        AutoSendToPricing = autoSendToPricing;
        AutoSendMinConfidence = NormalizeConfidence(autoSendMinConfidence);
        ProcessBodyWhenNoSupportedAttachments = processBodyWhenNoSupportedAttachments;
        ProcessBodyEvenWithAttachments = processBodyEvenWithAttachments;
        AllowedSenders = string.IsNullOrWhiteSpace(allowedSenders) ? null : allowedSenders.Trim();

        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void Delete(Guid? deletedBy = null)
    {
        MarkAsDeleted(DateTime.UtcNow, deletedBy?.ToString());
    }

    public void SetActive(bool isActive, Guid? updatedBy)
    {
        IsActive = isActive;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void MarkSyncSucceeded(long? lastProcessedUid, Guid? updatedBy = null)
    {
        if (lastProcessedUid.HasValue && (!LastProcessedUid.HasValue || lastProcessedUid.Value > LastProcessedUid.Value))
        {
            LastProcessedUid = lastProcessedUid.Value;
        }

        LastSyncAt = DateTime.UtcNow;
        LastSyncError = null;
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    public void MarkSyncFailed(string errorMessage, Guid? updatedBy = null)
    {
        LastSyncAt = DateTime.UtcNow;
        LastSyncError = string.IsNullOrWhiteSpace(errorMessage) ? "Error desconocido al leer el correo." : errorMessage.Trim();
        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());
    }

    private static string NormalizeRequired(string value, string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(errorMessage);
        }

        return value.Trim();
    }

    private static string NormalizeHost(EmailProviderType providerType, string? host)
    {
        return providerType switch
        {
            EmailProviderType.Gmail => "imap.gmail.com",
            EmailProviderType.Outlook => "outlook.office365.com",
            _ => NormalizeRequired(host ?? string.Empty, "El host IMAP es requerido para correos de dominio propio."),
        };
    }

    private static string NormalizeSecretReference(
        string secretReference,
        EmailProviderType providerType
    )
    {
        var reference = NormalizeRequired(
            secretReference,
            "La referencia del secreto del correo es requerida."
        );

        var compactValue = reference.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (
            providerType == EmailProviderType.Gmail
            && compactValue.Length == 16
            && compactValue.All(char.IsLetter)
        )
        {
            throw new InvalidOperationException(
                "SecretReference debe contener el nombre de una clave de configuración o "
                    + "variable de entorno, no una contraseña de aplicación de Gmail."
            );
        }

        return reference;
    }

    private static int DefaultPort(EmailProviderType providerType)
    {
        return providerType switch
        {
            EmailProviderType.Gmail or EmailProviderType.Outlook => 993,
            _ => 993,
        };
    }

    private static decimal NormalizeConfidence(decimal confidence)
    {
        if (confidence <= 0)
        {
            return 90m;
        }

        return Math.Clamp(confidence, 0m, 100m);
    }
}
