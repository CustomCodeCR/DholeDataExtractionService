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
            "pod",
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
    /// In tariff sources, POD means Port of Discharge and belongs to POE.
    /// DestinationPort is populated only by an explicit Place of Delivery or
    /// Final Destination header.
    /// </summary>
    public static string ResolveTargetField(string normalizedSourceHeader, string configuredTarget)
    {
        if (
            PoeSourceHeaders.Contains(normalizedSourceHeader)
        )
        {
            return PortOfExit;
        }

        if (FinalDestinationSourceHeaders.Contains(normalizedSourceHeader))
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
        return FinalDestinationSourceHeaders.Contains(normalizedSourceHeader);
    }
}
