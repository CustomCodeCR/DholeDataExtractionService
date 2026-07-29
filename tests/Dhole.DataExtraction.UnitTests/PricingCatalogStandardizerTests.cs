using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Application.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Entities;
using Dhole.DataExtraction.Infrastructure.Normalization;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class PricingCatalogStandardizerTests
{
    [TestMethod]
    public void PricingCatalogSlugs_ExposeOnlyTheEightKnownConfigGroups()
    {
        CollectionAssert.AreEquivalent(
            new[]
            {
                "carriers",
                "pol",
                "pod",
                "poe",
                "currencies",
                "agents",
                "container-types",
                "pricing-imports-profiles",
            },
            PricingCatalogSlugs.All.ToArray()
        );
    }

    [TestMethod]
    public async Task StandardizeAsync_ReplacesExtractedTextWithCanonicalConfigValues()
    {
        var items = new Dictionary<string, IReadOnlyCollection<ConfigCatalogItemResult>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            [PricingCatalogSlugs.Pol] =
            [
                Item(PricingCatalogSlugs.Pol, "SHA", "shanghai", "Shanghai", null,
                    "{\"aliases\":[\"Shanghai, China\",\"SHG\"]}")
            ],
            [PricingCatalogSlugs.Poe] =
            [
                Item(PricingCatalogSlugs.Poe, "CALDERA", "puerto-caldera", "Puerto Caldera", null,
                    "{\"aliases\":[\"Caldera\",\"Puerto Caldera, Costa Rica\"]}")
            ],
            [PricingCatalogSlugs.Pod] =
            [
                Item(PricingCatalogSlugs.Pod, "CALDERA", "puerto-caldera", "Puerto Caldera", null,
                    "{\"aliases\":[\"Caldera\",\"Puerto Caldera, Costa Rica\"]}")
            ],
            [PricingCatalogSlugs.ContainerTypes] =
            [
                Item(PricingCatalogSlugs.ContainerTypes, "40HC", "40-high-cube", "40 High Cube", null,
                    "{\"aliases\":[\"40' High Cube\",\"40HQ\"]}")
            ],
            [PricingCatalogSlugs.Carriers] =
            [
                Item(PricingCatalogSlugs.Carriers, "MSC", "msc", "Mediterranean Shipping Company", null,
                    "{\"aliases\":[\"MSC Line\",\"MSC\"]}")
            ],
            [PricingCatalogSlugs.Currencies] =
            [
                Item(PricingCatalogSlugs.Currencies, "USD", "usd", "Dólar estadounidense", "USD",
                    "{\"aliases\":[\"US Dollar\",\"US Dollars\"]}")
            ],
            [PricingCatalogSlugs.Agents] = [],
        };

        var record = PricingExtractionRecord.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Rates",
            2,
            "Shanghai, China",
            "Caldera",
            "Puerto Caldera, Costa Rica",
            "40' High Cube",
            "MSC Line",
            null,
            "General",
            "US Dollars",
            7,
            29,
            DateTime.UtcNow.Date,
            DateTime.UtcNow.Date.AddDays(30),
            1790m,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "{}",
            null
        );

        var standardizer = new PricingCatalogStandardizer(new FakeConfigCatalogClient(items));
        await standardizer.StandardizeAsync([record]);

        Assert.AreEqual("Shanghai", record.OriginPort);
        Assert.AreEqual("Puerto Caldera", record.PortOfExit);
        Assert.AreEqual("Puerto Caldera", record.DestinationPort);
        Assert.AreEqual("40 High Cube", record.ContainerType);
        Assert.AreEqual("Mediterranean Shipping Company", record.Carrier);
        Assert.AreEqual("Dólar estadounidense", record.Currency);

        Assert.IsNotNull(record.OriginPortReference);
        Assert.IsNotNull(record.PortOfExitReference);
        Assert.IsNotNull(record.DestinationPortReference);
        Assert.IsNotNull(record.ContainerTypeReference);
        Assert.IsNotNull(record.CarrierReference);
        Assert.IsNotNull(record.CurrencyReference);
        Assert.AreEqual("Shanghai, China", record.OriginPortReference.RawValue);
        Assert.AreEqual("MSC Line", record.CarrierReference.RawValue);
    }

    [TestMethod]
    public async Task StandardizeAsync_MatchesWhenConfigValueContainsIncomingPortName()
    {
        var items = new Dictionary<string, IReadOnlyCollection<ConfigCatalogItemResult>>(
            StringComparer.OrdinalIgnoreCase
        )
        {
            [PricingCatalogSlugs.Pol] =
            [
                Item(PricingCatalogSlugs.Pol, "SZX", "yantian-shenzhen", "Yantian (Shenzhen)", null, null)
            ],
            [PricingCatalogSlugs.Poe] =
            [
                Item(PricingCatalogSlugs.Poe, "MOIN", "puerto-de-moin", "Puerto de Moín", null, null)
            ],
            [PricingCatalogSlugs.Pod] =
            [
                Item(PricingCatalogSlugs.Pod, "COLMAN", "colon-manzanillo", "Colón/Manzanillo", null, null)
            ],
            [PricingCatalogSlugs.ContainerTypes] = [],
            [PricingCatalogSlugs.Carriers] = [],
            [PricingCatalogSlugs.Currencies] = [],
            [PricingCatalogSlugs.Agents] = [],
        };

        var record = PricingExtractionRecord.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Rates",
            2,
            "SHENZHEN",
            "MOIN",
            "MANZANILLO",
            "40HC",
            "MSC",
            null,
            null,
            "USD",
            7,
            30,
            DateTime.UtcNow.Date,
            DateTime.UtcNow.Date.AddDays(30),
            1200m,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "{}",
            null
        );

        await new PricingCatalogStandardizer(new FakeConfigCatalogClient(items))
            .StandardizeAsync([record]);

        Assert.AreEqual("Yantian (Shenzhen)", record.OriginPort);
        Assert.AreEqual("Puerto de Moín", record.PortOfExit);
        Assert.AreEqual("Colón/Manzanillo", record.DestinationPort);
        Assert.IsNotNull(record.OriginPortReference);
        Assert.IsNotNull(record.PortOfExitReference);
        Assert.IsNotNull(record.DestinationPortReference);
        Assert.AreEqual("SHENZHEN", record.OriginPortReference.RawValue);
        Assert.AreEqual("MOIN", record.PortOfExitReference.RawValue);
        Assert.AreEqual("MANZANILLO", record.DestinationPortReference.RawValue);
    }

    [TestMethod]
    public async Task StandardizeAsync_RepairsBrokenCharactersAndUsesPrimaryPortNames()
    {
        var items = PricingCatalogSlugs.RowCatalogs.ToDictionary(
            slug => slug,
            _ => (IReadOnlyCollection<ConfigCatalogItemResult>)[],
            StringComparer.OrdinalIgnoreCase
        );
        items[PricingCatalogSlugs.Pol] =
        [
            Item(PricingCatalogSlugs.Pol, "TSN", "tianjin", "Tianjin, China", null, null),
            Item(PricingCatalogSlugs.Pol, "XNG", "xingang", "Xingang, China", null, null),
            Item(PricingCatalogSlugs.Pol, "XMN", "xiamen", "Xiamen, China", null, null),
            Item(PricingCatalogSlugs.Pol, "YTN", "yantian", "Yantian (Shenzhen), China", null, null),
        ];
        items[PricingCatalogSlugs.Poe] =
        [
            Item(PricingCatalogSlugs.Poe, "MOIN", "moin", "Moín", null, null),
            Item(PricingCatalogSlugs.Poe, "PCR", "puerto-cortes", "Puerto Cortés", null, null),
        ];
        items[PricingCatalogSlugs.Agents] =
        [
            Item(
                PricingCatalogSlugs.Agents,
                "PGL",
                "pacific-global-logistics",
                "Pacific Global Logistics",
                null,
                null
            ),
        ];

        var first = PricingExtractionRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Rates", 2,
            "TIANJIN (XINGANG)", "MO�N", null, "40HC", "MSC",
            "Pacific Global Logistics S.A.", null, "USD",
            7, 30, DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(30),
            1200m, null, null, null, null, null, null, null, null, null, "{}", null
        );
        var second = PricingExtractionRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Rates", 3,
            "YANTIAN (SHENZHEN)", "PUERTO CORT�S", null, "40HC", "MSC",
            null, null, "USD",
            7, 30, DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(30),
            1200m, null, null, null, null, null, null, null, null, null, "{}", null
        );
        var third = PricingExtractionRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Rates", 4,
            "XIAMEN", null, null, "40HC", "MSC", null, null, "USD",
            7, 30, DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(30),
            1200m, null, null, null, null, null, null, null, null, null, "{}", null
        );

        await new PricingCatalogStandardizer(new FakeConfigCatalogClient(items))
            .StandardizeAsync([first, second, third]);

        Assert.AreEqual("Tianjin, China", first.OriginPort);
        Assert.AreNotEqual("Xingang, China", first.OriginPort);
        Assert.AreEqual("Moín", first.PortOfExit);
        Assert.AreEqual("Pacific Global Logistics", first.Agent);
        Assert.IsNotNull(first.AgentReference);
        Assert.AreEqual("Yantian (Shenzhen), China", second.OriginPort);
        Assert.AreEqual("Puerto Cortés", second.PortOfExit);
        Assert.AreEqual("Xiamen, China", third.OriginPort);
    }

    [TestMethod]
    public async Task StandardizeAsync_DoesNotFailWhenAConfigGroupHasNoItems()
    {
        var items = PricingCatalogSlugs.RowCatalogs.ToDictionary(
            slug => slug,
            _ => (IReadOnlyCollection<ConfigCatalogItemResult>)[],
            StringComparer.OrdinalIgnoreCase
        );

        var record = PricingExtractionRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Rates", 2,
            "SHANGHAI", "MOIN", "MOIN", "40HC", "MSC", null, null, "USD",
            7, 30, DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(30),
            1200m, null, null, null, null, null, null, null, null, null, "{}", null
        );

        await new PricingCatalogStandardizer(new FakeConfigCatalogClient(items))
            .StandardizeAsync([record]);

        Assert.AreEqual("SHANGHAI", record.OriginPort);
        Assert.IsNull(record.OriginPortReference);
        Assert.IsNull(record.PortOfExitReference);
        Assert.IsNull(record.DestinationPortReference);
    }

    [TestMethod]
    public async Task StandardizeAsync_MatchesMinorPortTyposAgainstConfig()
    {
        var items = PricingCatalogSlugs.RowCatalogs.ToDictionary(
            slug => slug,
            _ => (IReadOnlyCollection<ConfigCatalogItemResult>)[],
            StringComparer.OrdinalIgnoreCase
        );
        items[PricingCatalogSlugs.Pol] =
        [
            Item(
                PricingCatalogSlugs.Pol,
                "SZX",
                "yantian-shenzhen",
                "Yantian (Shenzhen)",
                null,
                null
            ),
        ];
        items[PricingCatalogSlugs.Poe] =
        [
            Item(
                PricingCatalogSlugs.Poe,
                "CALDERA",
                "puerto-caldera",
                "Puerto Caldera",
                null,
                null
            ),
        ];

        var record = PricingExtractionRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Rates", 2,
            "SHENZEN", "CALDER", null, "40HC", "MSC", null, null, "USD",
            7, 30, DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(30),
            1200m, null, null, null, null, null, null, null, null, null, "{}", null
        );

        await new PricingCatalogStandardizer(new FakeConfigCatalogClient(items))
            .StandardizeAsync([record]);

        Assert.AreEqual("Yantian (Shenzhen)", record.OriginPort);
        Assert.AreEqual("Puerto Caldera", record.PortOfExit);
        Assert.IsNotNull(record.OriginPortReference);
        Assert.IsNotNull(record.PortOfExitReference);
    }

    [TestMethod]
    public async Task StandardizeAsync_DoesNotGuessAgentFromPartialCompanyName()
    {
        var items = PricingCatalogSlugs.RowCatalogs.ToDictionary(
            slug => slug,
            _ => (IReadOnlyCollection<ConfigCatalogItemResult>)[],
            StringComparer.OrdinalIgnoreCase
        );
        items[PricingCatalogSlugs.Agents] =
        [
            Item(
                PricingCatalogSlugs.Agents,
                "PGL",
                "pacific-global-logistics",
                "Pacific Global Logistics S.A.",
                null,
                "{\"aliases\":[\"PGL Costa Rica\"]}"
            ),
        ];

        var record = PricingExtractionRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Rates", 2,
            "SHANGHAI", "MOIN", null, "40HC", "MSC", "Global Logistics", null, "USD",
            7, 30, DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(30),
            1200m, null, null, null, null, null, null, null, null, null, "{}", null
        );

        await new PricingCatalogStandardizer(new FakeConfigCatalogClient(items))
            .StandardizeAsync([record]);

        Assert.IsNull(record.AgentReference);
        Assert.AreEqual("Global Logistics", record.Agent);

        var exactAliasRecord = PricingExtractionRecord.Create(
            Guid.NewGuid(), Guid.NewGuid(), "Rates", 3,
            "SHANGHAI", "MOIN", null, "40HC", "MSC", "PGL Costa Rica", null, "USD",
            7, 30, DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(30),
            1200m, null, null, null, null, null, null, null, null, null, "{}", null
        );

        await new PricingCatalogStandardizer(new FakeConfigCatalogClient(items))
            .StandardizeAsync([exactAliasRecord]);

        Assert.IsNotNull(exactAliasRecord.AgentReference);
        Assert.AreEqual("Pacific Global Logistics S.A.", exactAliasRecord.Agent);
    }

    private static ConfigCatalogItemResult Item(
        string group,
        string code,
        string slug,
        string name,
        string? value,
        string? metadataJson
    ) => new(
        Guid.NewGuid(),
        group,
        code,
        slug,
        name,
        value,
        metadataJson,
        true
    );

    private sealed class FakeConfigCatalogClient(
        IReadOnlyDictionary<string, IReadOnlyCollection<ConfigCatalogItemResult>> items
    ) : IConfigCatalogClient
    {
        public Task<ConfigCatalogItemResult?> ResolveCatalogItemAsync(
            string catalogGroupSlug,
            string value,
            CancellationToken cancellationToken = default
        )
        {
            var result = items.TryGetValue(catalogGroupSlug, out var groupItems)
                ? groupItems.FirstOrDefault(item =>
                    item.Code.Equals(value, StringComparison.OrdinalIgnoreCase)
                    || item.Slug.Equals(value, StringComparison.OrdinalIgnoreCase)
                    || item.Name.Equals(value, StringComparison.OrdinalIgnoreCase)
                )
                : null;

            return Task.FromResult(result);
        }

        public Task<bool> ValidateCatalogItemAsync(
            string catalogGroupSlug,
            string catalogItemSlug,
            CancellationToken cancellationToken = default
        )
        {
            var isValid = items.TryGetValue(catalogGroupSlug, out var groupItems)
                && groupItems.Any(item => item.Slug.Equals(
                    catalogItemSlug,
                    StringComparison.OrdinalIgnoreCase
                ));
            return Task.FromResult(isValid);
        }

        public Task<IReadOnlyCollection<ConfigCatalogItemResult>> GetActiveCatalogItemsByGroupAsync(
            string catalogGroupSlug,
            CancellationToken cancellationToken = default
        )
        {
            return Task.FromResult(
                items.TryGetValue(catalogGroupSlug, out var groupItems)
                    ? groupItems
                    : (IReadOnlyCollection<ConfigCatalogItemResult>)[]
            );
        }
    }
}
