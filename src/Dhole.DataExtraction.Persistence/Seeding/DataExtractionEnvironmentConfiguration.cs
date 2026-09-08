using Microsoft.Extensions.Configuration;

namespace Dhole.DataExtraction.Persistence.Seeding;

public static class DataExtractionEnvironmentConfiguration
{
    public static IReadOnlyDictionary<string, string?> BuildOverrides(IConfiguration configuration)
    {
        var overrides = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        Copy(configuration, overrides, "DATA_EXTRACTION_EMAIL_ENABLED", "EmailIngestion:Enabled");

        var username = Read(configuration, "DATA_EXTRACTION_EMAIL_USERNAME");
        if (!string.IsNullOrWhiteSpace(username))
        {
            overrides["EmailIngestion:SeedAccounts:0:emailAddress"] = username;
            overrides["EmailIngestion:SeedAccounts:0:username"] = username;
        }

        if (!string.IsNullOrWhiteSpace(Read(configuration, "DATA_EXTRACTION_EMAIL_PASSWORD")))
        {
            // Never persist the password itself. The database stores only the env key name.
            overrides["EmailIngestion:SeedAccounts:0:secretReference"] = "DATA_EXTRACTION_EMAIL_PASSWORD";
        }

        return overrides;
    }

    public static bool IsEmailIngestionEnabled(IConfiguration configuration)
    {
        var value = Read(configuration, "DATA_EXTRACTION_EMAIL_ENABLED")
            ?? Read(configuration, "EmailIngestion:Enabled");

        return bool.TryParse(value, out var enabled) && enabled;
    }

    public static string? EmailUsername(IConfiguration configuration)
    {
        return Read(configuration, "DATA_EXTRACTION_EMAIL_USERNAME")
            ?? Read(configuration, "EmailIngestion:SeedAccounts:0:username")
            ?? Read(configuration, "EmailIngestion:SeedAccounts:0:emailAddress");
    }

    private static void Copy(
        IConfiguration configuration,
        IDictionary<string, string?> destination,
        string sourceKey,
        string targetKey
    )
    {
        var value = Read(configuration, sourceKey);
        if (!string.IsNullOrWhiteSpace(value))
        {
            destination[targetKey] = value;
        }
    }

    private static string? Read(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
