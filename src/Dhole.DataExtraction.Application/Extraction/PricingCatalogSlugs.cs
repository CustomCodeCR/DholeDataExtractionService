namespace Dhole.DataExtraction.Application.Extraction;

public static class PricingCatalogSlugs
{
    public const string Carriers = "carriers";
    public const string Pol = "pol";
    public const string Poe = "poe";
    public const string Pod = "pod";
    public const string Currencies = "currencies";
    public const string Agents = "agents";
    public const string ContainerTypes = "container-types";
    public const string ContainerSizes = "container-sizes";
    public const string ContainerKinds = "container-kinds";
    public const string LandEquipmentTypes = "land-equipment-types";
    public const string LandEquipmentSizes = "land-equipment-sizes";
    public const string LandEquipmentKinds = "land-equipment-kinds";
    public const string ImportProfiles = "pricing-imports-profiles";

    /// <summary>
    /// The only Config groups used by the Pricing extraction integration.
    /// Keep this list explicit so neither AI output nor document headers can
    /// invent a catalog slug dynamically.
    /// </summary>
    public static readonly IReadOnlyCollection<string> All =
    [
        Carriers,
        Pol,
        Pod,
        Poe,
        Currencies,
        Agents,
        ContainerTypes,
        ContainerSizes,
        ContainerKinds,
        LandEquipmentTypes,
        LandEquipmentSizes,
        LandEquipmentKinds,
        ImportProfiles,
    ];

    public static readonly IReadOnlyCollection<string> RowCatalogs =
    [
        Carriers,
        Pol,
        Pod,
        Poe,
        Currencies,
        Agents,
        ContainerTypes,
        LandEquipmentTypes,
    ];

    public static bool IsKnown(string? slug)
    {
        return !string.IsNullOrWhiteSpace(slug)
            && All.Contains(slug.Trim(), StringComparer.OrdinalIgnoreCase);
    }
}
