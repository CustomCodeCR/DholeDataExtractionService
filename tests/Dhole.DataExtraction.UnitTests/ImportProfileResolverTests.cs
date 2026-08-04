using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Application.Extraction;
using Dhole.DataExtraction.Infrastructure.Mapping;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class ImportProfileResolverTests
{
    [TestMethod]
    public async Task ResolveAsync_SelectsOnlyActiveProfileWhenCodeIsNotProvided()
    {
        var expected = Item("STD", "standard", "Estándar", "STANDARD");
        var resolver = new ImportProfileResolver(new FakeConfigCatalogClient([expected]));

        var result = await resolver.ResolveAsync(null);

        Assert.AreEqual(expected.Id, result.Item.Id);
        Assert.AreEqual("STANDARD", result.MappingProfileCode);
        Assert.AreEqual("STANDARD", result.RawValue);
    }

    [TestMethod]
    public async Task ResolveAsync_SelectsStandardProfileWhenSeveralAreActive()
    {
        var vendor = Item("VENDOR", "vendor-a", "Proveedor A", "VENDOR_A");
        var expected = Item("STD", "standard", "Perfil estándar", "STANDARD");
        var resolver = new ImportProfileResolver(
            new FakeConfigCatalogClient([vendor, expected])
        );

        var result = await resolver.ResolveAsync(string.Empty);

        Assert.AreEqual(expected.Id, result.Item.Id);
        Assert.AreEqual("STANDARD", result.MappingProfileCode);
    }

    [TestMethod]
    public async Task ResolveAsync_RespectsExplicitRegisteredProfile()
    {
        var standard = Item("STD", "standard", "Estándar", "STANDARD");
        var vendor = Item("VENDOR", "vendor-a", "Proveedor A", "VENDOR_A");
        var resolver = new ImportProfileResolver(
            new FakeConfigCatalogClient([standard, vendor])
        );

        var result = await resolver.ResolveAsync("vendor-a");

        Assert.AreEqual(vendor.Id, result.Item.Id);
        Assert.AreEqual("VENDOR_A", result.MappingProfileCode);
        Assert.AreEqual("vendor-a", result.RawValue);
    }

    [TestMethod]
    public async Task ResolveAsync_FailsWhenMultipleProfilesHaveNoAutomaticDefault()
    {
        var resolver = new ImportProfileResolver(
            new FakeConfigCatalogClient(
                [
                    Item("A", "vendor-a", "Proveedor A", "VENDOR_A"),
                    Item("B", "vendor-b", "Proveedor B", "VENDOR_B"),
                ]
            )
        );

        var exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => resolver.ResolveAsync(null)
        );

        StringAssert.Contains(exception.Message, "ninguno puede identificarse como estándar");
    }

    private static ConfigCatalogItemResult Item(
        string code,
        string slug,
        string name,
        string? value,
        string? metadataJson = null
    ) => new(
        Guid.NewGuid(),
        PricingCatalogSlugs.ImportProfiles,
        code,
        slug,
        name,
        value,
        metadataJson,
        true
    );

    private sealed class FakeConfigCatalogClient(
        IReadOnlyCollection<ConfigCatalogItemResult> profiles
    ) : IConfigCatalogClient
    {
        public Task<ConfigCatalogItemResult?> ResolveCatalogItemAsync(
            string catalogGroupSlug,
            string value,
            CancellationToken cancellationToken = default
        )
        {
            var result = catalogGroupSlug.Equals(
                    PricingCatalogSlugs.ImportProfiles,
                    StringComparison.OrdinalIgnoreCase
                )
                ? profiles.FirstOrDefault(item =>
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
        ) => Task.FromResult(false);

        public Task<IReadOnlyCollection<ConfigCatalogItemResult>> GetActiveCatalogItemsByGroupAsync(
            string catalogGroupSlug,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(
            catalogGroupSlug.Equals(
                PricingCatalogSlugs.ImportProfiles,
                StringComparison.OrdinalIgnoreCase
            )
                ? profiles
                : (IReadOnlyCollection<ConfigCatalogItemResult>)[]
        );
    }
}
