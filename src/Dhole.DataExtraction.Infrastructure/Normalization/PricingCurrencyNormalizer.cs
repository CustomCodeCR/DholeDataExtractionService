using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Dhole.DataExtraction.Infrastructure.Files;

namespace Dhole.DataExtraction.Infrastructure.Normalization;

public static class PricingCurrencyNormalizer
{
    public const string DefaultCurrency = "USD";

    private static readonly IReadOnlyDictionary<string, string> Aliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = "USD",
            ["US DOLLAR"] = "USD",
            ["US DOLLARS"] = "USD",
            ["DOLLAR"] = "USD",
            ["DOLLARS"] = "USD",
            ["DOLAR"] = "USD",
            ["DOLARES"] = "USD",
            ["CRC"] = "CRC",
            ["COLON"] = "CRC",
            ["COLONES"] = "CRC",
            ["COSTA RICAN COLON"] = "CRC",
            ["EUR"] = "EUR",
            ["EURO"] = "EUR",
            ["EUROS"] = "EUR",
            ["GBP"] = "GBP",
            ["POUND"] = "GBP",
            ["POUNDS"] = "GBP",
            ["STERLING"] = "GBP",
            ["CNY"] = "CNY",
            ["RMB"] = "CNY",
            ["YUAN"] = "CNY",
            ["CAD"] = "CAD",
            ["MXN"] = "MXN",
            ["COP"] = "COP",
            ["PAB"] = "PAB",
            ["JPY"] = "JPY",
        };

    public static string NormalizeOrDefault(string? value)
    {
        return TryNormalizeExplicit(value) ?? DefaultCurrency;
    }

    public static string? TryNormalizeExplicit(string? value)
    {
        var cleaned = string.IsNullOrWhiteSpace(value)
            ? null
            : TextContentDecoder.Clean(value).Trim();

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        if (cleaned.Contains('₡'))
        {
            return "CRC";
        }

        if (cleaned.Contains('€'))
        {
            return "EUR";
        }

        if (cleaned.Contains('£'))
        {
            return "GBP";
        }

        // En Pricing, un monto expresado únicamente con "$" se interpreta como USD
        // salvo que la misma fuente indique explícitamente otra moneda.
        if (cleaned.Contains('$'))
        {
            return "USD";
        }

        var normalizedText = Regex.Replace(
            RemoveDiacritics(cleaned).ToUpperInvariant(),
            @"[^A-Z]+",
            " "
        ).Trim();
        if (Aliases.TryGetValue(normalizedText, out var aliasCurrency))
        {
            return aliasCurrency;
        }

        var codeMatch = Regex.Match(
            normalizedText,
            @"\b(USD|EUR|CRC|GBP|JPY|CNY|RMB|CAD|MXN|COP|PAB)\b",
            RegexOptions.IgnoreCase
        );

        if (codeMatch.Success)
        {
            var code = codeMatch.Groups[1].Value.ToUpperInvariant();
            return code == "RMB" ? "CNY" : code;
        }

        // Se conserva una moneda explícita no reconocida para que Config pueda
        // resolverla o dejarla en revisión; solo la ausencia real usa USD.
        var compact = Regex.Replace(cleaned.ToUpperInvariant(), @"\s+", " ").Trim();
        return compact.Length <= 20
            && compact.Any(char.IsLetter)
            && !compact.Any(char.IsDigit)
                ? compact
                : null;
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
