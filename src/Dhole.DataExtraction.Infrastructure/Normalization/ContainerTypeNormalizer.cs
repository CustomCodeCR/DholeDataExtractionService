namespace Dhole.DataExtraction.Infrastructure.Normalization;

public sealed record ContainerSelection(
    string EquipmentCode,
    string SizeCode,
    string KindCode,
    string KindName
);

public static class ContainerTypeNormalizer
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var parsed = Parse(value);
        if (parsed is not null)
        {
            return parsed.EquipmentCode;
        }

        return Clean(value);
    }

    /// <summary>
    /// Separates an extracted equipment value into its size and semantic kind while
    /// retaining the legacy/canonical equipment code consumed by Pricing.
    /// </summary>
    public static ContainerSelection? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var clean = Clean(value);
        var size = ReadSize(clean);
        if (size is null)
        {
            return null;
        }

        var kind = ReadKind(clean, size);
        if (kind is null)
        {
            return null;
        }

        return new ContainerSelection(
            $"{size}{kind.Value.Code}",
            size,
            kind.Value.Code,
            kind.Value.Name
        );
    }

    private static string Clean(string value)
    {
        return new string(
            value
                .Trim()
                .ToUpperInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray()
        );
    }

    private static string? ReadSize(string clean)
    {
        if (clean.StartsWith("20", StringComparison.Ordinal)) return "20";
        if (clean.StartsWith("40", StringComparison.Ordinal)) return "40";
        if (clean.StartsWith("45", StringComparison.Ordinal)) return "45";
        if (clean.StartsWith("48", StringComparison.Ordinal)) return "48";
        return null;
    }

    private static (string Code, string Name)? ReadKind(string clean, string size)
    {
        var suffix = clean[size.Length..];

        if (
            suffix.Contains("NONOPERATINGREEFER", StringComparison.Ordinal)
            || suffix.Contains("NONOPREEFER", StringComparison.Ordinal)
            || suffix == "NOR"
            || suffix.StartsWith("NOR", StringComparison.Ordinal)
        )
        {
            return ("NOR", "NOR");
        }

        if (
            suffix.Contains("OPENTOP", StringComparison.Ordinal)
            || suffix == "OT"
            || suffix.StartsWith("OT", StringComparison.Ordinal)
        )
        {
            return ("OT", "Open Top");
        }

        if (
            suffix.Contains("OPENSIDE", StringComparison.Ordinal)
            || suffix.Contains("SIDEOPEN", StringComparison.Ordinal)
            || suffix == "OS"
            || suffix.StartsWith("OS", StringComparison.Ordinal)
        )
        {
            return ("OS", "Open Side");
        }

        if (
            suffix.Contains("FLATRACK", StringComparison.Ordinal)
            || suffix == "FR"
            || suffix.StartsWith("FR", StringComparison.Ordinal)
        )
        {
            return ("FR", "Flat Rack");
        }

        if (
            suffix.Contains("ISOTANK", StringComparison.Ordinal)
            || suffix.Contains("TANK", StringComparison.Ordinal)
            || suffix.StartsWith("TNK", StringComparison.Ordinal)
            || suffix.StartsWith("TK", StringComparison.Ordinal)
        )
        {
            return ("TK", "Tank");
        }

        if (
            suffix.Contains("HIGHCUBE", StringComparison.Ordinal)
            || suffix.StartsWith("HC", StringComparison.Ordinal)
            || suffix.StartsWith("HQ", StringComparison.Ordinal)
        )
        {
            return ("HC", "High Cube");
        }

        if (
            suffix.Length == 0 && (size is "20" or "40")
            || suffix.StartsWith("DRY", StringComparison.Ordinal)
            || suffix.StartsWith("DV", StringComparison.Ordinal)
            || suffix.StartsWith("DC", StringComparison.Ordinal)
            || suffix.StartsWith("GP", StringComparison.Ordinal)
            || suffix.StartsWith("STD", StringComparison.Ordinal)
            || suffix.StartsWith("STANDARD", StringComparison.Ordinal)
            || suffix.StartsWith("FT", StringComparison.Ordinal)
            || suffix is "ST" or "SV"
        )
        {
            return ("DV", "Dry Van");
        }

        return null;
    }
}
