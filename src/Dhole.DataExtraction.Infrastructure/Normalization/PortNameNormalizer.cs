namespace Dhole.DataExtraction.Infrastructure.Normalization;

public static class PortNameNormalizer
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
            "PTO CALDERA" or "PUERTO CALDERA" => "CALDERA",
            "PUERTO LIMON" or "PTO LIMON" or "LIMON" => "PUERTO LIMON",
            "SHANGHAI PORT" => "SHANGHAI",
            _ => clean
        };
    }
}
