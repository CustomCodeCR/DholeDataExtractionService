using System.Text.RegularExpressions;

namespace Dhole.DataExtraction.Infrastructure.Mapping;

/// <summary>
/// Expands a carrier heading or extracted equipment value into one canonical
/// row per physical container type. A shared amount such as 40SV/40HC is copied
/// to two rows; the rows are never merged.
/// </summary>
public static class PricingContainerVariants
{
    public static IReadOnlyList<string> Expand(string? value)
    {
        var normalized = ColumnHeaderNormalizer.Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        var result = new List<string>();

        void Add(string containerType)
        {
            if (!result.Contains(containerType, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(containerType);
            }
        }

        var has20 = Regex.IsMatch(normalized, @"(^|[^0-9])20([^0-9]|$)")
            || normalized.StartsWith("20", StringComparison.Ordinal)
            || normalized.Contains("20gp", StringComparison.Ordinal)
            || normalized.Contains("20dc", StringComparison.Ordinal)
            || normalized.Contains("20dv", StringComparison.Ordinal)
            || normalized.Contains("20std", StringComparison.Ordinal)
            || normalized.Contains("20ft", StringComparison.Ordinal)
            || normalized.Contains("20dry", StringComparison.Ordinal);

        var has40Hc = normalized.Contains("40hc", StringComparison.Ordinal)
            || normalized.Contains("40hq", StringComparison.Ordinal)
            || normalized.Contains("40highcube", StringComparison.Ordinal);

        var hasExplicit40Dry = normalized.Contains("40gp", StringComparison.Ordinal)
            || normalized.Contains("40dc", StringComparison.Ordinal)
            || normalized.Contains("40dv", StringComparison.Ordinal)
            || normalized.Contains("40std", StringComparison.Ordinal)
            || normalized.Contains("40st", StringComparison.Ordinal)
            || normalized.Contains("40sv", StringComparison.Ordinal)
            || normalized.Contains("40ft", StringComparison.Ordinal)
            || normalized.Contains("40dry", StringComparison.Ordinal)
            || normalized.Contains("40standard", StringComparison.Ordinal);

        var hasBare40 = normalized == "40"
            || Regex.IsMatch(
                normalized,
                @"^40(usd|eur|crc|rate|rates|freight|flete|tarifa|amount|costo|venta|sale|allin|oceanfreight)?$"
            );

        var hasCompound40And40Hc = has40Hc
            && (
                value?.Contains('/') == true
                || value?.Contains('\\') == true
                || normalized.StartsWith("4040", StringComparison.Ordinal)
                || Regex.IsMatch(normalized, @"40(?:gp|dv|dc|std|st|sv)?(?:y|and)?40hc")
            );

        var hasPlain40 = hasExplicit40Dry || hasBare40 || hasCompound40And40Hc;
        var has45Hc = normalized.Contains("45hc", StringComparison.Ordinal)
            || normalized.Contains("45hq", StringComparison.Ordinal);

        if (has20)
        {
            Add("20DV");
        }

        if (hasPlain40)
        {
            Add("40DV");
        }

        if (has40Hc)
        {
            Add("40HC");
        }

        if (has45Hc)
        {
            Add("45HC");
        }

        return result;
    }
}
