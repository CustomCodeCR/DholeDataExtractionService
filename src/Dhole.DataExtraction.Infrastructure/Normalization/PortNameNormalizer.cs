using Dhole.DataExtraction.Infrastructure.Files;
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

        var clean = TextContentDecoder.Clean(value).Trim().ToUpperInvariant();

        return clean switch
        {
            "CHINA BASE PORTS" or "CHINA BASE PORT" or "BASE PORTS CHINA"
                or "BASE PORT CHINA" or "ASIA BASE PORTS" or "ASIA BASE PORT" =>
                string.Join("/", PricingBasePorts.China),
            "NINGBO PORT" or "NGB" => "NINGBO",
            "SHANGHAI PORT" or "SHA" or "SHG" => "SHANGHAI",
            "SHENZHEN PORT" or "SZN" or "SZX" => "SHENZHEN",
            "XIAMEN PORT" or "XMN" => "XIAMEN",
            "QINGDAO PORT" or "TAO" => "QINGDAO",
            "TIANJIN PORT" or "XINGANG" or "TSN" => "TIANJIN (XINGANG)",
            "DALIAN PORT" or "DLN" or "DLC" => "DALIAN",
            "PTO CALDERA" or "PUERTO CALDERA" or "CALDERA, COSTA RICA" => "CALDERA",
            "ACAJULTA" or "ACAJUTLA, EL SALVADOR" => "ACAJUTLA",
            "CORINTO, NICARAGUA" => "CORINTO",
            "PUERTO LIMON" or "PTO LIMON" or "LIMON" or "LIMÓN" => "PUERTO LIMON",
            "MOIN" or "MOÍN" or "PUERTO MOIN" or "PUERTO MOÍN" => "MOIN",
            _ => clean,
        };
    }
}
