namespace Dhole.DataExtraction.Infrastructure.Normalization;

public static class ContainerTypeNormalizer
{
    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var clean = new string(
            value
                .Trim()
                .ToUpperInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray()
        );

        return clean switch
        {
            "20" or "20DRY" or "20DV" or "20GP" or "20STD" or "20STANDARD"
                or "20STANDARDDRY" or "20DRYCONTAINER" or "20GENERALPURPOSE" => "20DV",
            "40" or "40DRY" or "40DV" or "40GP" or "40STD" or "40STANDARD"
                or "40STANDARDDRY" or "40DRYCONTAINER" or "40GENERALPURPOSE" => "40DV",
            "40HC" or "40HQ" or "40HIGHCUBE" or "40HIGHCUBECONTAINER" => "40HC",
            "45HC" or "45HQ" or "45HIGHCUBE" or "45HIGHCUBECONTAINER" => "45HC",
            _ => clean,
        };
    }
}
