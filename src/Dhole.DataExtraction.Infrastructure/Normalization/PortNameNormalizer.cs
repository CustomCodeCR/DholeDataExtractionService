using Dhole.DataExtraction.Infrastructure.Mapping;

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
            "CHINA BASE PORTS" or "CHINA BASE PORT" or "BASE PORTS CHINA"
                or "BASE PORT CHINA" or "ASIA BASE PORTS" or "ASIA BASE PORT" =>
                string.Join("/", PricingBasePorts.China),
            "NINGBO PORT" or "NGB" => "NINGBO",
            "SHANGHAI PORT" or "SHA" or "SHG" => "SHANGHAI",
            "QINGDAO PORT" or "TAO" => "QINGDAO",
            "PTO CALDERA" or "PUERTO CALDERA" or "CALDERA, COSTA RICA" => "CALDERA",
            "PUERTO LIMON" or "PTO LIMON" or "LIMON" or "LIMÓN" => "PUERTO LIMON",
            "MOIN" or "MOÍN" or "PUERTO MOIN" or "PUERTO MOÍN" => "MOIN",
            _ => clean,
        };
    }
}
