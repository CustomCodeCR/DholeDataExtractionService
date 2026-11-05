using System.Text.RegularExpressions;

namespace Dhole.DataExtraction.Domain.Emails;

/// <summary>
/// Conservative gate for tariff emails. Only FCL, LCL, FTL, LTL and air freight
/// are supported. A missing modality is not evidence of invalidity when the
/// document is unreadable or could still contain a tariff.
/// </summary>
public static class EmailTariffModalityPolicy
{
    public const string InvalidReason =
        "Tarifa no válida: el contenido no corresponde a tarifas FCL, LCL, FTL, LTL o aéreas.";

    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled;
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(300);

    private static readonly Regex ExplicitMode = new(
        @"\b(?:FCL|LCL|FTL|LTL|AIR\s*FREIGHT|AIR\s*CARGO|AIR\s*RATE|AIR\s*RATES|AIR\s*TRANSPORT|AIRLINE|AWB|MAWB|HAWB|A[ÉE]RE[OA]S?|A[ÉE]REA|A[ÉE]REO|AEROL[IÍ]NEA|CARGA\s+A[ÉE]REA|FLETE\s+A[ÉE]REO|FULL\s*CONTAINER(?:\s*LOAD)?|LESS\s*THAN\s*CONTAINER(?:\s*LOAD)?|FULL\s*TRUCK(?:LOAD)?|LESS\s*THAN\s*TRUCK(?:LOAD)?|CARGA\s+COMPLETA\s+(?:POR\s+)?CAMI[ÓO]N|CAMI[ÓO]N\s+COMPLETO|CARGA\s+CONSOLIDADA|CONSOLIDACI[ÓO]N\s+MAR[IÍ]TIMA)\b",
        Options, Timeout);

    // Common FCL container codes, including the 40NOR seen in carrier rate emails.
    private static readonly Regex ContainerEquipment = new(
        @"\b(?:[1-9]\s*[xX]\s*)?(?:20|40|45)\s*['’]?(?:GP|DC|DV|HC|HQ|NOR|RF|RH|OT|FR|REEFER)\b|\b(?:20|40|45)\s*['’]\s*(?:CONTAINER|CONTENEDOR)?\b",
        Options, Timeout);

    private static readonly Regex LclEvidence = new(
        @"\b(?:W\s*/\s*M|WM|CBM|CFT|RT|REVENUE\s*TON|TONELADA\s*FACTURABLE|CARGA\s*SUELTA|LCL\s*MIAMI|CONSOLIDADO\s*MAR[IÍ]TIMO)\b",
        Options, Timeout);

    private static readonly Regex RoadEvidence = new(
        @"\b(?:ROAD\s*FREIGHT|TRUCKING\s*RATES?|TRUCKLOAD|FLETE\s*TERRESTRE|TRANSPORTE\s*TERRESTRE|FLETE\s*POR\s*CARRETERA|CARGA\s*CONSOLIDADA\s*TERRESTRE)\b",
        Options, Timeout);

    private static readonly Regex RouteEvidence = new(
        @"\b(?:POL|POD|POE|PORT\s*OF\s*LOADING|PORT\s*OF\s*DISCHARGE)\b",
        Options, Timeout);
    private static readonly Regex MaritimeRateEvidence = new(
        @"\b(?:OCEAN\s*FREIGHT|SEA\s*FREIGHT|NAVIERA|CARRIER|FLETE\s*MAR[IÍ]TIMO|FREIGHT\s*RATE|FLETE\s*INTERNACIONAL)\b",
        Options, Timeout);
    private static readonly Regex AmountEvidence = new(
        @"(?:\b(?:USD|EUR|CRC|US\$)(?=\s*[\d.,])|[$€₡])\s*[\d.,]+|\b(?:FREIGHT\s*AMOUNT|OCEAN\s*FREIGHT|RATE\s*AMOUNT|FREIGHT|FLETE|RATE|TARIFA)\s*[:=]?\s*[\d.,]+",
        Options, Timeout);

    public static bool HasSupportedModality(string? evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence)) return false;
        return ExplicitMode.IsMatch(evidence)
            || ContainerEquipment.IsMatch(evidence)
            || LclEvidence.IsMatch(evidence)
            || RoadEvidence.IsMatch(evidence)
            // A quoted NAC/FAK carrier matrix may have no literal "FCL".
            || (RouteEvidence.IsMatch(evidence)
                && MaritimeRateEvidence.IsMatch(evidence)
                && AmountEvidence.IsMatch(evidence));
    }

    private static readonly Regex RateOfferEvidence = new(
        @"\b(?:TARIFAS?|TARIFARIOS?|COTIZACI[ÓO]N|QUOTATION|OFFER|PRICING|RATES?|FREIGHT|FLETE|SURCHARGES?|RECARGOS?)\b",
        Options, Timeout);

    private static readonly Regex AdministrativeDocument = new(
        @"\b(?:PACKING\s*LIST|LISTA\s+DE\s+EMPAQUE|FACTURA|INVOICE|BILL\s+OF\s+LADING|BOOKING\s+CONFIRMATION|AVISO\s+DE\s+LLEGADA|ARRIVAL\s+NOTICE|MANIFIESTO|SHIPMENT\s+STATUS)\b",
        Options, Timeout);

    public static bool IsReadableAndUnrelated(string? extractedContent)
    {
        if (string.IsNullOrWhiteSpace(extractedContent)) return false;
        var value = extractedContent.Trim();
        // Never classify a scanned/unreadable document as invalid just because
        // MarkItDown could not obtain the text.
        if (value.Length < 60) return false;

        // A packing list can contain "CBM", or an invoice can name "FCL".
        // Neither is a freight tariff without an actual rate offer.
        if (AdministrativeDocument.IsMatch(value)
            && !RateOfferEvidence.IsMatch(value))
            return true;

        if (HasSupportedModality(value))
            return !RateOfferEvidence.IsMatch(value) && !AmountEvidence.IsMatch(value);

        // A generic "freight/rates" mention without a recognizable mode may
        // still be a real quotation. Leave it for human review, without retry.
        if (MaritimeRateEvidence.IsMatch(value) || RouteEvidence.IsMatch(value))
            return false;

        return true;
    }
}
