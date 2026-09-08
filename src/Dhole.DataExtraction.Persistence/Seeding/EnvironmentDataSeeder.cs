using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Dhole.DataExtraction.Persistence.Seeding;

public static class EnvironmentDataSeeder
{
    public static async Task<EnvironmentSeedResult> SynchronizeAsync(
        ServiceDbContext dbContext,
        IConfiguration configuration,
        CancellationToken cancellationToken = default
    )
    {
        var username = DataExtractionEnvironmentConfiguration.EmailUsername(configuration);
        if (string.IsNullOrWhiteSpace(username))
        {
            return new EnvironmentSeedResult(false, null, false);
        }

        var enabled = DataExtractionEnvironmentConfiguration.IsEmailIngestionEnabled(configuration);
        var section = configuration.GetSection("EmailIngestion:SeedAccounts:0");
        var emailAddress = username.Trim().ToLowerInvariant();
        var configuredEmail = Read(section, "emailAddress")?.ToLowerInvariant();

        var providerText = Read(section, "providerType") ?? "Gmail";
        if (!Enum.TryParse<EmailProviderType>(providerText, true, out var providerType))
        {
            providerType = EmailProviderType.Gmail;
        }

        var account = await dbContext.EmailIngestionAccounts.FirstOrDefaultAsync(
            item => item.EmailAddress == emailAddress && !item.IsDeleted,
            cancellationToken
        );

        // If the env address changed, reuse the old seeded account instead of creating duplicates.
        if (account is null && !string.IsNullOrWhiteSpace(configuredEmail))
        {
            account = await dbContext.EmailIngestionAccounts.FirstOrDefaultAsync(
                item => item.EmailAddress == configuredEmail && !item.IsDeleted,
                cancellationToken
            );
        }

        var name = Read(section, "name") ?? "Buzón tarifas";
        var host = Read(section, "host");
        var port = PositiveInt(section, "port", 993);
        var useSsl = Boolean(section, "useSsl", true);
        var secretReference = "DATA_EXTRACTION_EMAIL_PASSWORD";
        var folderName = Read(section, "folderName") ?? "INBOX";
        var pollingIntervalMinutes = PositiveInt(section, "pollingIntervalMinutes", 5);
        var autoProcess = Boolean(section, "autoProcess", true);
        var autoSendToPricing = Boolean(section, "autoSendToPricing", true);
        var autoSendMinConfidence = Decimal(section, "autoSendMinConfidence", 90m);
        var processBodyWhenNoSupportedAttachments = Boolean(
            section,
            "processBodyWhenNoSupportedAttachments",
            true
        );
        var processBodyEvenWithAttachments = Boolean(
            section,
            "processBodyEvenWithAttachments",
            true
        );
        var allowedSenders = Read(section, "allowedSenders");

        if (account is null)
        {
            account = EmailIngestionAccount.Create(
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
                null
            );
            account.SetActive(enabled, null);
            dbContext.EmailIngestionAccounts.Add(account);
        }
        else
        {
            account.Update(
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
                null
            );
            account.SetActive(enabled, null);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new EnvironmentSeedResult(true, account.EmailAddress, account.IsActive);
    }

    private static string? Read(IConfigurationSection section, string key)
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

    public sealed record EnvironmentSeedResult(bool EmailAccountRestored, string? EmailAddress, bool EmailEnabled);
}
