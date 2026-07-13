using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Dhole.DataExtraction.Persistence.Seeding;

public static class EmailIngestionAccountSeeder
{
    private static readonly Guid LegacySeedAccountId = Guid.Parse(
        "4f1bd7f0-dc89-4d21-a4d0-12c2e7f2c311"
    );

    public static async Task SynchronizeAsync(
        ServiceDbContext dbContext,
        IConfiguration configuration,
        CancellationToken cancellationToken = default
    )
    {
        var accountSections = configuration
            .GetSection("EmailIngestion:SeedAccounts")
            .GetChildren()
            .ToArray();

        if (accountSections.Length == 0)
        {
            return;
        }

        var synchronizedAccountIds = new HashSet<Guid>();

        foreach (var section in accountSections)
        {
            var settings = ReadSettings(section);
            var account = await dbContext.EmailIngestionAccounts.FirstOrDefaultAsync(
                x => x.EmailAddress == settings.EmailAddress && !x.IsDeleted,
                cancellationToken
            );

            var legacyAccount = await dbContext.EmailIngestionAccounts.FirstOrDefaultAsync(
                x => x.Id == LegacySeedAccountId && !x.IsDeleted,
                cancellationToken
            );

            if (account is null && legacyAccount is not null)
            {
                account = legacyAccount;
            }
            else if (
                account is not null
                && legacyAccount is not null
                && account.Id != legacyAccount.Id
            )
            {
                legacyAccount.Delete();
            }

            if (account is null)
            {
                account = EmailIngestionAccount.Create(
                    settings.Name,
                    settings.EmailAddress,
                    settings.ProviderType,
                    settings.Host,
                    settings.Port,
                    settings.UseSsl,
                    settings.Username,
                    settings.SecretReference,
                    settings.FolderName,
                    settings.PollingIntervalMinutes,
                    settings.AutoProcess,
                    settings.AutoSendToPricing,
                    settings.AutoSendMinConfidence,
                    settings.ProcessBodyWhenNoSupportedAttachments,
                    settings.ProcessBodyEvenWithAttachments,
                    settings.AllowedSenders,
                    null
                );

                dbContext.EmailIngestionAccounts.Add(account);
            }
            else
            {
                account.Update(
                    settings.Name,
                    settings.EmailAddress,
                    settings.ProviderType,
                    settings.Host,
                    settings.Port,
                    settings.UseSsl,
                    settings.Username,
                    settings.SecretReference,
                    settings.FolderName,
                    settings.PollingIntervalMinutes,
                    settings.AutoProcess,
                    settings.AutoSendToPricing,
                    settings.AutoSendMinConfidence,
                    settings.ProcessBodyWhenNoSupportedAttachments,
                    settings.ProcessBodyEvenWithAttachments,
                    settings.AllowedSenders,
                    null
                );

                account.SetActive(true, null);
            }

            synchronizedAccountIds.Add(account.Id);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await RecoverAutomaticProcessingAsync(
            dbContext,
            synchronizedAccountIds.ToArray(),
            cancellationToken
        );
    }

    private static async Task RecoverAutomaticProcessingAsync(
        ServiceDbContext dbContext,
        IReadOnlyCollection<Guid> accountIds,
        CancellationToken cancellationToken
    )
    {
        if (accountIds.Count == 0)
        {
            return;
        }

        var accounts = await dbContext.EmailIngestionAccounts
            .Where(x => accountIds.Contains(x.Id) && x.IsActive && !x.IsDeleted)
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var messagesWithoutJobs = await dbContext.EmailMessages
            .Where(x =>
                accountIds.Contains(x.EmailIngestionAccountId)
                && x.Status == EmailMessageStatus.NeedsReview
                && x.ErrorMessage != null
                && x.ErrorMessage.StartsWith(
                    "La cuenta está configurada para revisión manual"
                )
                && !x.IsDeleted
                && !dbContext.EmailExtractionJobs.Any(job =>
                    job.EmailMessageId == x.Id && !job.IsDeleted
                )
            )
            .ToListAsync(cancellationToken);

        foreach (var message in messagesWithoutJobs)
        {
            if (
                !accounts.TryGetValue(message.EmailIngestionAccountId, out var account)
                || !account.AutoProcess
            )
            {
                continue;
            }

            var attachments = await dbContext.EmailAttachments
                .Where(x => x.EmailMessageId == message.Id && !x.IsDeleted)
                .ToListAsync(cancellationToken);

            var supportedAttachments = attachments
                .Where(x =>
                    x.SourceFileType
                        is SourceFileType.Excel
                            or SourceFileType.Csv
                            or SourceFileType.Pdf
                            or SourceFileType.Email
                )
                .ToArray();
            var jobsCreated = false;

            foreach (var attachment in supportedAttachments)
            {
                dbContext.EmailExtractionJobs.Add(
                    EmailExtractionJob.CreateAttachmentJob(message.Id, attachment.Id)
                );
                jobsCreated = true;
            }

            if (
                account.ProcessBodyEvenWithAttachments
                || (
                    supportedAttachments.Length == 0
                    && account.ProcessBodyWhenNoSupportedAttachments
                    && (
                        !string.IsNullOrWhiteSpace(message.BodyHtml)
                        || !string.IsNullOrWhiteSpace(message.BodyText)
                    )
                )
            )
            {
                dbContext.EmailExtractionJobs.Add(
                    EmailExtractionJob.CreateBodyJob(message.Id)
                );
                jobsCreated = true;
            }

            if (jobsCreated)
            {
                message.MarkQueued(
                    message.ClassificationConfidence ?? 50m,
                    "Procesamiento automático habilitado; correo puesto nuevamente en cola."
                );
            }
        }

        var retryableJobs = await dbContext.EmailExtractionJobs
            .Where(x =>
                (
                    x.Status == EmailExtractionJobStatus.NeedsReview
                    || (
                        x.Status == EmailExtractionJobStatus.Processing
                        && x.StartedAt < DateTime.UtcNow.AddMinutes(-30)
                    )
                )
                && !x.IsDeleted
            )
            .ToListAsync(cancellationToken);

        foreach (var job in retryableJobs)
        {
            var message = await dbContext.EmailMessages.FirstOrDefaultAsync(
                x => x.Id == job.EmailMessageId && !x.IsDeleted,
                cancellationToken
            );

            if (
                message is null
                || !accounts.TryGetValue(message.EmailIngestionAccountId, out var account)
                || !account.AutoProcess
                || !account.AutoSendToPricing
            )
            {
                continue;
            }

            var isAbandoned = job.Status == EmailExtractionJobStatus.Processing;
            var meetsAutomaticThreshold =
                job.ConfidenceScore.HasValue
                && job.ConfidenceScore.Value >= account.AutoSendMinConfidence;
            var shouldReevaluateAfterDeliveryFix =
                job.Status == EmailExtractionJobStatus.NeedsReview
                && !string.IsNullOrWhiteSpace(job.ErrorMessage)
                && (
                    job.ErrorMessage.StartsWith(
                        "Extracción correcta con confianza",
                        StringComparison.OrdinalIgnoreCase
                    )
                    || job.ErrorMessage.Contains(
                        "Pricing",
                        StringComparison.OrdinalIgnoreCase
                    )
                );

            if (isAbandoned || meetsAutomaticThreshold || shouldReevaluateAfterDeliveryFix)
            {
                job.Retry();
                message.MarkQueued(
                    message.ClassificationConfidence ?? job.ConfidenceScore ?? 50m,
                    isAbandoned
                        ? "Se recuperó un trabajo de extracción interrumpido."
                        : shouldReevaluateAfterDeliveryFix
                            ? "El trabajo se puso nuevamente en cola para aplicar la corrección de envío a Pricing."
                            : "Envío automático a Pricing habilitado; trabajo puesto nuevamente en cola."
                );
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static AccountSettings ReadSettings(IConfigurationSection section)
    {
        var providerText = Required(section, "providerType");
        if (!Enum.TryParse<EmailProviderType>(providerText, true, out var providerType))
        {
            throw new InvalidOperationException(
                $"EmailIngestion:SeedAccounts contiene el proveedor no soportado '{providerText}'."
            );
        }

        return new AccountSettings(
            Required(section, "name"),
            Required(section, "emailAddress").ToLowerInvariant(),
            providerType,
            Optional(section, "host"),
            PositiveInt(section, "port", 993),
            Boolean(section, "useSsl", true),
            Required(section, "username"),
            Required(section, "secretReference"),
            Optional(section, "folderName") ?? "INBOX",
            PositiveInt(section, "pollingIntervalMinutes", 5),
            Boolean(section, "autoProcess", true),
            Boolean(section, "autoSendToPricing", true),
            Decimal(section, "autoSendMinConfidence", 90m),
            Boolean(section, "processBodyWhenNoSupportedAttachments", true),
            Boolean(section, "processBodyEvenWithAttachments", false),
            Optional(section, "allowedSenders")
        );
    }

    private static string Required(IConfigurationSection section, string key)
    {
        return Optional(section, key)
            ?? throw new InvalidOperationException(
                $"Falta EmailIngestion:SeedAccounts:{section.Key}:{key}."
            );
    }

    private static string? Optional(IConfigurationSection section, string key)
    {
        var value = section[key];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int PositiveInt(IConfigurationSection section, string key, int fallback)
    {
        return int.TryParse(section[key], out var value) && value > 0 ? value : fallback;
    }

    private static bool Boolean(IConfigurationSection section, string key, bool fallback)
    {
        return bool.TryParse(section[key], out var value) ? value : fallback;
    }

    private static decimal Decimal(IConfigurationSection section, string key, decimal fallback)
    {
        return decimal.TryParse(section[key], out var value) ? value : fallback;
    }

    private sealed record AccountSettings(
        string Name,
        string EmailAddress,
        EmailProviderType ProviderType,
        string? Host,
        int Port,
        bool UseSsl,
        string Username,
        string SecretReference,
        string FolderName,
        int PollingIntervalMinutes,
        bool AutoProcess,
        bool AutoSendToPricing,
        decimal AutoSendMinConfidence,
        bool ProcessBodyWhenNoSupportedAttachments,
        bool ProcessBodyEvenWithAttachments,
        string? AllowedSenders
    );
}
