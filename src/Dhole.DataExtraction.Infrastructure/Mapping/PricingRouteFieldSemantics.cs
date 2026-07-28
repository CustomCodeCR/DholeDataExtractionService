namespace Dhole.DataExtraction.Infrastructure.Mapping;

/// <summary>
/// Keeps the business meaning of the three route fields independent from the
/// wording used by carriers in spreadsheets, PDFs and emails.
/// </summary>
public static class PricingRouteFieldSemantics
{
    public const string OriginPort = "OriginPort";
    public const string PortOfExit = "PortOfExit";
    public const string DestinationPort = "DestinationPort";

    private static readonly HashSet<string> PoeSourceHeaders = new(
        [
            "poe",
            "portofexit",
            "exitport",
            "puertosalida",
            "portofentry",
            "entryport",
            "puertoentrada",
            "destination",
            "destino",
            "destinationport",
            "puertodestino",
            "portdischarge",
            "portofdischarge",
            "discharge",
            "dischargeport",
            "arrivalport",
            "portofarrival",
            "gateway",
            "costaricagateway",
            "transshipmentport",
            "transshipment",
            "transbordo",
            "via",
            "to",
        ],
        StringComparer.OrdinalIgnoreCase
    );

    private static readonly HashSet<string> FinalDestinationSourceHeaders = new(
        [
            "placeofdelivery",
            "delivery",
            "deliveryplace",
            "deliverypoint",
            "finaldestination",
            "finaldelivery",
            "destinofinal",
            "lugardeentrega",
            "puntodeentrega",
        ],
        StringComparer.OrdinalIgnoreCase
    );

    /// <summary>
    /// Corrects legacy/profile mappings that treated a maritime destination
    /// header as POD. Imported tariff destinations, including headers named POD,
    /// always represent the route's POE. The POD of the official Dhole rate is
    /// selected manually after import and is never inferred from the source.
    /// </summary>
    public static string ResolveTargetField(string normalizedSourceHeader, string configuredTarget)
    {
        if (
            PoeSourceHeaders.Contains(normalizedSourceHeader)
            || normalizedSourceHeader.Equals("pod", StringComparison.OrdinalIgnoreCase)
            || FinalDestinationSourceHeaders.Contains(normalizedSourceHeader)
        )
        {
            return PortOfExit;
        }

        return configuredTarget;
    }

    public static bool IsPoeSourceHeader(string normalizedSourceHeader)
    {
        return PoeSourceHeaders.Contains(normalizedSourceHeader);
    }

    public static bool IsPodSourceHeader(string normalizedSourceHeader)
    {
        return normalizedSourceHeader.Equals("pod", StringComparison.OrdinalIgnoreCase)
            || FinalDestinationSourceHeaders.Contains(normalizedSourceHeader);
    }
}
