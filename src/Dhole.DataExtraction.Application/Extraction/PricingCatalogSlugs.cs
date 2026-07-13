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
    public const string ImportProfiles = "pricing-imports-profiles";

    public static readonly IReadOnlyCollection<string> RowCatalogs =
    [
        Carriers,
        Pol,
        Poe,
        Pod,
        Currencies,
        Agents,
        ContainerTypes,
    ];
}
