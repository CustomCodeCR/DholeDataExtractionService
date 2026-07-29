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
    /// Keeps POE and POD separate. Destination/Port of Discharge belongs to POE,
    /// while an explicit POD/Place of Delivery/Final Destination belongs to the
    /// distinct DestinationPort field.
    /// </summary>
    public static string ResolveTargetField(string normalizedSourceHeader, string configuredTarget)
    {
        if (
            PoeSourceHeaders.Contains(normalizedSourceHeader)
        )
        {
            return PortOfExit;
        }

        if (
            normalizedSourceHeader.Equals("pod", StringComparison.OrdinalIgnoreCase)
            || FinalDestinationSourceHeaders.Contains(normalizedSourceHeader)
        )
        {
            return DestinationPort;
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
