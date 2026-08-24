using System.Text.RegularExpressions;
using Dhole.DataExtraction.Infrastructure.Normalization;

namespace Dhole.DataExtraction.Infrastructure.Mapping;

/// <summary>
/// Expands a carrier heading or extracted equipment value into one canonical
/// row per physical container equipment. Shared headings such as 40DV/HC are
/// expanded into independent rows and extended equipment aliases are normalized.
/// </summary>
public static class PricingContainerVariants
{
    private static readonly char[] Separators = ['/', '\\', '|', ',', ';'];

    public static IReadOnlyList<string> Expand(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var result = new List<string>();

        void Add(string? containerType)
        {
            if (
                !string.IsNullOrWhiteSpace(containerType)
                && !result.Contains(containerType, StringComparer.OrdinalIgnoreCase)
            )
            {
                result.Add(containerType);
            }
        }

        var parts = value.Split(Separators, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        string? inheritedSize = null;

        foreach (var part in parts)
        {
            var partSize = ReadLeadingSize(part);
            if (partSize is not null)
            {
                inheritedSize = partSize;
            }

            var candidate = partSize is null && inheritedSize is not null
                ? inheritedSize + part
                : part;

            var parsed = ContainerTypeNormalizer.Parse(candidate);
            Add(parsed?.EquipmentCode);
        }

        var normalized = ColumnHeaderNormalizer.Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return result;
        }

        // Preserve support for compact carrier headings where separators are
        // stripped before mapping (for example 40DV/HC -> 40dvhc).
        if (Regex.IsMatch(normalized, @"40(?:gp|dv|dc|std|st|sv|ft|dry|standard)(?:and|y)?(?:40)?(?:hc|hq|highcube)"))
        {
            Add("40DV");
            Add("40HC");
        }

        foreach (var size in new[] { "20", "40", "45", "48" })
        {
            AddIfPresent(size, "OT", ["ot", "opentop"]);
            AddIfPresent(size, "OS", ["os", "openside", "sideopen"]);
            AddIfPresent(size, "FR", ["fr", "flatrack"]);
            AddIfPresent(size, "TK", ["tk", "tnk", "tank", "isotank"]);
            AddIfPresent(size, "NOR", ["nor", "nonoperatingreefer", "nonopreefer"]);
            AddIfPresent(size, "HC", ["hc", "hq", "highcube"]);
        }

        // Generic 20/40 headings continue to mean standard dry equipment.
        if (result.Count == 0)
        {
            var parsed = ContainerTypeNormalizer.Parse(value);
            Add(parsed?.EquipmentCode);
        }

        return result;

        void AddIfPresent(string size, string kind, IReadOnlyCollection<string> aliases)
        {
            if (aliases.Any(alias => normalized.Contains(size + alias, StringComparison.Ordinal)))
            {
                Add(size + kind);
            }
        }
    }

    private static string? ReadLeadingSize(string value)
    {
        var clean = new string(value.Trim().Where(char.IsLetterOrDigit).ToArray());
        if (clean.StartsWith("20", StringComparison.Ordinal)) return "20";
        if (clean.StartsWith("40", StringComparison.Ordinal)) return "40";
        if (clean.StartsWith("45", StringComparison.Ordinal)) return "45";
        if (clean.StartsWith("48", StringComparison.Ordinal)) return "48";
        return null;
    }
}
