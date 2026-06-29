namespace Dhole.DataExtraction.Infrastructure.Normalization;

public static class ContainerTypeNormalizer
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var clean = value.Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "");

        return clean switch
        {
            "20" or "20DRY" or "20DV" or "20GP" or "20STD" => "20DV",
            "40" or "40DRY" or "40DV" or "40GP" or "40STD" => "40DV",
            "40HC" or "40HQ" or "40HIGHCUBE" => "40HC",
            "45HC" or "45HQ" => "45HC",
            _ => clean,
        };
    }
}
