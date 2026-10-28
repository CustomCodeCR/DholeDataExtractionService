using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Application.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Entities;
using Dhole.DataExtraction.Infrastructure.Normalization;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class ManzanilloPortNormalizationTests
{
    [DataTestMethod]
    [DataRow("Manzanillo", "MANZANILLO, PANAMA")]
    [DataRow("Puerto Manzanillo", "MANZANILLO, PANAMA")]
    [DataRow("Manzanillo, Panamá", "MANZANILLO, PANAMA")]
    [DataRow("Manzanillo, Colón", "MANZANILLO, PANAMA")]
    [DataRow("Manzanillo, México", "MANZANILLO, MEXICO")]
    [DataRow("Manzanillo, Colima", "MANZANILLO, MEXICO")]
    [DataRow("Manzanillo, MX", "MANZANILLO, MEXICO")]
    public void PortNameNormalizer_AppliesPanamaDefaultButRespectsExplicitMexico(
        string input, string expected
    )
    {
        Assert.AreEqual(expected, PortNameNormalizer.Normalize(input));
    }

    [TestMethod]
    public void PortNameNormalizer_DoesNotCollapseMultiPortList()
    {
        Assert.AreEqual("MANZANILLO/VERACRUZ",
            PortNameNormalizer.Normalize("Manzanillo/Veracruz"));
    }

    [TestMethod]
    public async Task RecordNormalizer_AppliesDefaultConsistentlyToPolPoeAndPod()
    {
        var row = new MappedPricingRow(
            "Rates", 2,
            new Dictionary<string, string?>
            {
                ["OriginPort"] = "Manzanillo",
                ["PortOfExit"] = "Puerto Manzanillo",
                ["DestinationPort"] = "Manzanillo, Panamá",
                ["ContainerType"] = "40HC",
                ["Carrier"] = "MSC",
                ["OceanFreight"] = "1000"
            }
        );

        var records = await new PricingRecordNormalizer().NormalizeAsync(
            Guid.NewGuid(), Guid.NewGuid(), [row]
        );
        var record = records.Single();
        Assert.AreEqual("MANZANILLO, PANAMA", record.OriginPort);
        Assert.AreEqual("MANZANILLO, PANAMA", record.PortOfExit);
        Assert.AreEqual("MANZANILLO, PANAMA", record.DestinationPort);
    }

    [TestMethod]
    public async Task CatalogMatcher_PrefersPanamaForAmbiguousManzanilloInAllThreeGroups()
    {
        var items = EmptyCatalogs();
        foreach (var group in new[] { PricingCatalogSlugs.Pol, PricingCatalogSlugs.Poe, PricingCatalogSlugs.Pod })
        {
            items[group] =
            [
                Item(group, "MXZLO", "manzanillo-mexico", "Manzanillo, México",
                    "{\"aliases\":[\"Manzanillo\",\"Puerto Manzanillo\"]}"),
                Item(group, "PAMIT", "colon-manzanillo-panama", "Colón/Manzanillo, Panamá",
                    "{\"aliases\":[\"Manzanillo\",\"MIT\"]}"),
            ];
        }

        var record = Record("MANZANILLO", "PUERTO MANZANILLO", "MANZANILLO, PANAMA");
        await new PricingCatalogStandardizer(new FakeCatalog(items)).StandardizeAsync([record]);

        Assert.AreEqual("Colón/Manzanillo, Panamá", record.OriginPort);
        Assert.AreEqual("Colón/Manzanillo, Panamá", record.PortOfExit);
        Assert.AreEqual("Colón/Manzanillo, Panamá", record.DestinationPort);
        Assert.IsNotNull(record.OriginPortReference);
        Assert.IsNotNull(record.PortOfExitReference);
        Assert.IsNotNull(record.DestinationPortReference);
        Assert.AreEqual("MANZANILLO", record.OriginPortReference.RawValue);
        Assert.AreEqual("PUERTO MANZANILLO", record.PortOfExitReference.RawValue);
        Assert.AreEqual("MANZANILLO, PANAMA", record.DestinationPortReference.RawValue);
    }

    [TestMethod]
    public async Task CatalogMatcher_DoesNotResolveMultiPortListAsSingleManzanillo()
    {
        var items = EmptyCatalogs();
        items[PricingCatalogSlugs.Poe] =
        [
            Item(PricingCatalogSlugs.Poe, "PAMIT", "manzanillo-panama",
                "Manzanillo, Panamá", null)
        ];
        var record = Record(null, "Manzanillo/Veracruz", null);
        await new PricingCatalogStandardizer(new FakeCatalog(items)).StandardizeAsync([record]);
        Assert.IsNull(record.PortOfExitReference);
        Assert.AreEqual("Manzanillo/Veracruz", record.PortOfExit);
    }

    [TestMethod]
    public async Task CatalogMatcher_ExplicitMexicoIsRespected()
    {
        var items = EmptyCatalogs();
        items[PricingCatalogSlugs.Poe] =
        [
            Item(PricingCatalogSlugs.Poe, "MXZLO", "manzanillo-mexico",
                "Manzanillo, México", null),
            Item(PricingCatalogSlugs.Poe, "PAMIT", "manzanillo-panama",
                "Manzanillo, Panamá", null)
        ];
        var record = Record(null, "MANZANILLO, MEXICO", null);
        await new PricingCatalogStandardizer(new FakeCatalog(items)).StandardizeAsync([record]);
        Assert.AreEqual("Manzanillo, México", record.PortOfExit);
        Assert.IsNotNull(record.PortOfExitReference);
    }

    [TestMethod]
    public async Task CatalogMatcher_DoesNotFallBackToMexicoWhenPanamaIsMissing()
    {
        var items = EmptyCatalogs();
        items[PricingCatalogSlugs.Poe] =
        [
            Item(PricingCatalogSlugs.Poe, "MXZLO", "manzanillo-mexico",
                "Manzanillo, México", "{\"aliases\":[\"Manzanillo\"]}")
        ];
        var record = Record(null, "MANZANILLO, PANAMA", null);
        await new PricingCatalogStandardizer(new FakeCatalog(items)).StandardizeAsync([record]);
        Assert.IsNull(record.PortOfExitReference);
        Assert.AreEqual("MANZANILLO, PANAMA", record.PortOfExit);
    }

    private static Dictionary<string, IReadOnlyCollection<ConfigCatalogItemResult>> EmptyCatalogs()
        => PricingCatalogSlugs.RowCatalogs.ToDictionary(
            group => group,
            _ => (IReadOnlyCollection<ConfigCatalogItemResult>)[],
            StringComparer.OrdinalIgnoreCase
        );

    private static ConfigCatalogItemResult Item(
        string group, string code, string slug, string name, string? aliases
    ) => new(Guid.NewGuid(), group, code, slug, name, null, aliases, true);

    private static PricingExtractionRecord Record(string? pol, string? poe, string? pod)
        => PricingExtractionRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Rates", 2,
            pol, poe, pod, "40HC", "MSC", null, null, "USD",
            7, 20, DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(30),
            1000m, null, null, null, null, null, null, null, null, null,
            "{}", null
        );

    private sealed class FakeCatalog(
        IReadOnlyDictionary<string, IReadOnlyCollection<ConfigCatalogItemResult>> items
    ) : IConfigCatalogClient
    {
        public Task<ConfigCatalogItemResult?> ResolveCatalogItemAsync(
            string catalogGroupSlug, string value,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(items.TryGetValue(catalogGroupSlug, out var list)
            ? list.FirstOrDefault(i => i.Code == value || i.Name == value)
            : null);

        public Task<bool> ValidateCatalogItemAsync(
            string catalogGroupSlug, string catalogItemSlug,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(items.TryGetValue(catalogGroupSlug, out var list)
            && list.Any(item => item.Slug == catalogItemSlug));

        public Task<IReadOnlyCollection<ConfigCatalogItemResult>> GetActiveCatalogItemsByGroupAsync(
            string catalogGroupSlug, CancellationToken cancellationToken = default
        ) => Task.FromResult(items.TryGetValue(catalogGroupSlug, out var list)
            ? list : (IReadOnlyCollection<ConfigCatalogItemResult>)[]);
    }
}
