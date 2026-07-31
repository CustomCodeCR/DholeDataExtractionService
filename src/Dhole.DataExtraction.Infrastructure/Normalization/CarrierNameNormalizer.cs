using System.Text.RegularExpressions;

namespace Dhole.DataExtraction.Infrastructure.Normalization;

public static class CarrierNameNormalizer
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var clean = value.Trim().ToUpperInvariant();

        // FAK/Basket describe the commercial product, not the shipping line.
        // Keep the original value in RawJson, but normalize the carrier so it can
        // match the Config catalog without requiring AI.
        clean = Regex.Replace(
            clean,
            @"\s+(?:FAK|BASKET|SPOT|PREMIUM)\s*$",
            string.Empty,
            RegexOptions.IgnoreCase
        ).Trim();

        return clean switch
        {
            "MSK" or "MAERSK" or "MAERSK LINE" or "MAEU" => "MAERSK",
            "CMA" or "CMA-CGM" or "CMA CGM" => "CMA CGM",
            "HPL" or "HAPAG" or "HAPAG LLOYD" or "HAPAG-LLOYD" => "HAPAG-LLOYD",
            "MSC LINE" or "MEDITERRANEAN SHIPPING COMPANY" => "MSC",
            "ONE" or "ONE LINE" or "OCEAN NETWORK EXPRESS" => "ONE",
            "COSCO" or "COSCO SHIPPING" => "COSCO",
            "EVERGREEN" or "EMC" => "EVERGREEN",
            "YML" or "YANG MING" or "YANG MING LINE" => "YANG MING",
            "ZIM LINE" => "ZIM",
            _ => clean,
        };
    }
}
