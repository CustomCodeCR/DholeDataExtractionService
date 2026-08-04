using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Application.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Entities;
using Dhole.DataExtraction.Infrastructure.Email;
using Dhole.DataExtraction.Infrastructure.Normalization;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class EmailAgentResolverTests
{
    [TestMethod]
    public void NormalizeForExtraction_RemovesForwardPrefixAndNormalizesSeparators()
    {
        var normalized = EmailSubjectNormalizer.NormalizeForExtraction(
            "RV: CASTRO FALLS// WWL CONTRACT ONE-MSC / AUG"
        );

        Assert.AreEqual("CASTRO FALLS | WWL CONTRACT ONE-MSC | AUG", normalized);
    }

    [TestMethod]
    public async Task ApplyFromEmailAsync_ResolvesRegisteredAgentFromNoisySubjectWithMinorTypo()
    {
        var config = CreateConfigClient();
        var record = CreateRecord(agent: null);
        var resolver = new EmailAgentResolver(
            config,
            NullLogger<EmailAgentResolver>.Instance
        );

        await resolver.ApplyFromEmailAsync(
            [record],
            "RV: CASTRO FALLS// WWL CONTRACT ONE-MSC / AUG",
            null,
            null
        );
        await new PricingCatalogStandardizer(config).StandardizeAsync([record]);

        Assert.AreEqual("Castro Fallas S.A.", record.Agent);
        Assert.IsNotNull(record.AgentReference);
        Assert.AreEqual("castro-fallas", record.AgentReference.Slug);
    }

    [TestMethod]
    public async Task ApplyFromEmailAsync_UsesBodyOnlyWhenSubjectDoesNotIdentifyAgent()
    {
        var config = CreateConfigClient();
        var record = CreateRecord(agent: "Agente desconocido");
        var resolver = new EmailAgentResolver(
            config,
            NullLogger<EmailAgentResolver>.Instance
        );

        await resolver.ApplyFromEmailAsync(
            [record],
            "Tarifas FCL agosto",
            "Favor procesar las tarifas de Pacific Global Logistics para Caldera.",
            null
        );

        Assert.AreEqual("Pacific Global Logistics S.A.", record.Agent);
    }

    [TestMethod]
    public async Task ApplyFromEmailAsync_SubjectAgentOverridesAgentInExtractedRow()
    {
        var config = CreateConfigClient();
        var record = CreateRecord(agent: "Pacific Global Logistics S.A.");
        var resolver = new EmailAgentResolver(
            config,
            NullLogger<EmailAgentResolver>.Instance
        );

        await resolver.ApplyFromEmailAsync(
            [record],
            "RV: CASTRO FALLS// WWL CONTRACT ONE-MSC / AUG",
            null,
            null
        );

        Assert.AreEqual("Castro Fallas S.A.", record.Agent);
    }

    [TestMethod]
    public async Task ApplyFromEmailAsync_PreservesRegisteredRowAgentWhenEmailHasNoAgent()
    {
        var config = CreateConfigClient();
        var record = CreateRecord(agent: "Pacific Global Logistics S.A.");
        var resolver = new EmailAgentResolver(
            config,
            NullLogger<EmailAgentResolver>.Instance
        );

        await resolver.ApplyFromEmailAsync(
            [record],
            "Tarifas FCL agosto",
            "Vigencia del 8 al 14 de agosto.",
            null
        );

        Assert.AreEqual("Pacific Global Logistics S.A.", record.Agent);
    }

    private static PricingExtractionRecord CreateRecord(string? agent)
    {
        return PricingExtractionRecord.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Rates",
            2,
            "Shanghai",
            "Puerto Caldera",
            null,
            "40HC",
            "MSC",
            agent,
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
    }

    private static IConfigCatalogClient CreateConfigClient()
    {
        var items = PricingCatalogSlugs.RowCatalogs.ToDictionary(
            slug => slug,
            _ => (IReadOnlyCollection<ConfigCatalogItemResult>)[],
            StringComparer.OrdinalIgnoreCase
        );
        items[PricingCatalogSlugs.Agents] =
        [
            Item("CF", "castro-fallas", "Castro Fallas S.A.", null),
            Item(
                "PGL",
                "pacific-global-logistics",
                "Pacific Global Logistics S.A.",
                null
            ),
        ];
        return new FakeConfigCatalogClient(items);
    }

    private static ConfigCatalogItemResult Item(
        string code,
        string slug,
        string name,
        string? value
    ) => new(
        Guid.NewGuid(),
        PricingCatalogSlugs.Agents,
        code,
        slug,
        name,
        value,
        null,
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
                    || string.Equals(item.Value, value, StringComparison.OrdinalIgnoreCase)
                )
                : null;
            return Task.FromResult(result);
        }

        public Task<bool> ValidateCatalogItemAsync(
            string catalogGroupSlug,
            string catalogItemSlug,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(
            items.TryGetValue(catalogGroupSlug, out var groupItems)
            && groupItems.Any(item => item.Slug.Equals(
                catalogItemSlug,
                StringComparison.OrdinalIgnoreCase
            ))
        );

        public Task<IReadOnlyCollection<ConfigCatalogItemResult>> GetActiveCatalogItemsByGroupAsync(
            string catalogGroupSlug,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(
            items.TryGetValue(catalogGroupSlug, out var groupItems)
                ? groupItems
                : (IReadOnlyCollection<ConfigCatalogItemResult>)[]
        );
    }
}
