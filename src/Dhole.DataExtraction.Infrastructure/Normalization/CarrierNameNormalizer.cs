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

        return clean switch
        {
            "MAERSK LINE" or "MAEU" => "MAERSK",
            "CMA" or "CMA-CGM" => "CMA CGM",
            "HAPAG" or "HAPAG LLOYD" => "HAPAG-LLOYD",
            "MSC LINE" => "MSC",
            _ => clean,
        };
    }
}
