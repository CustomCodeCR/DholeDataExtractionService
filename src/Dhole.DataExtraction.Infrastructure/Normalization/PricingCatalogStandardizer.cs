using System.Globalization;
using System.Text;
using System.Text.Json;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Application.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Entities;
using Dhole.DataExtraction.Domain.Extraction.ValueObjects;

namespace Dhole.DataExtraction.Infrastructure.Normalization;

public sealed class PricingCatalogStandardizer(IConfigCatalogClient configCatalogClient)
    : IPricingCatalogStandardizer
{
    public async Task StandardizeAsync(
        IReadOnlyCollection<PricingExtractionRecord> records,
        Guid? updatedBy = null,
        CancellationToken cancellationToken = default
    )
    {
        if (records.Count == 0)
        {
            return;
        }

        var catalogTasks = PricingCatalogSlugs.RowCatalogs.ToDictionary(
            slug => slug,
            slug => configCatalogClient.GetActiveCatalogItemsByGroupAsync(
                slug,
                cancellationToken
            )
        );

        await Task.WhenAll(catalogTasks.Values);

        var catalogs = catalogTasks.ToDictionary(
            pair => pair.Key,
            pair => BuildLookup(pair.Value.Result),
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();

            record.ApplyCatalogReferences(
                Resolve(catalogs[PricingCatalogSlugs.Pol], record.OriginPort),
                Resolve(catalogs[PricingCatalogSlugs.Poe], record.PortOfExit),
                Resolve(catalogs[PricingCatalogSlugs.Pod], record.DestinationPort),
                Resolve(catalogs[PricingCatalogSlugs.ContainerTypes], record.ContainerType),
                Resolve(catalogs[PricingCatalogSlugs.Carriers], record.Carrier),
                Resolve(catalogs[PricingCatalogSlugs.Agents], record.Agent),
                Resolve(catalogs[PricingCatalogSlugs.Currencies], record.Currency),
                updatedBy
            );
        }
    }

    private static IReadOnlyDictionary<string, ConfigCatalogItemResult> BuildLookup(
        IReadOnlyCollection<ConfigCatalogItemResult> items
    )
    {
        var lookup = new Dictionary<string, ConfigCatalogItemResult>(
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var item in items.Where(x => x.IsActive))
        {
            AddKey(lookup, item.Id.ToString(), item);
            AddKey(lookup, item.Code, item);
            AddKey(lookup, item.Slug, item);
            AddKey(lookup, item.Name, item);
            AddKey(lookup, item.Value, item);

            foreach (var alias in ReadAliases(item.MetadataJson))
            {
                AddKey(lookup, alias, item);
            }
        }

        return lookup;
    }

    private static void AddKey(
        IDictionary<string, ConfigCatalogItemResult> lookup,
        string? value,
        ConfigCatalogItemResult item
    )
    {
        var key = NormalizeLookupKey(value);
        if (!string.IsNullOrWhiteSpace(key))
        {
            lookup.TryAdd(key, item);
        }
    }

    private static CatalogItemReference? Resolve(
        IReadOnlyDictionary<string, ConfigCatalogItemResult> catalog,
        string? rawValue
    )
    {
        var key = NormalizeLookupKey(rawValue);
        if (string.IsNullOrWhiteSpace(key) || !catalog.TryGetValue(key, out var item))
        {
            return null;
        }

        return CatalogItemReference.Create(
            item.Id,
            item.CatalogGroupSlug,
            item.Code,
            item.Slug,
            item.Name,
            rawValue
        );
    }

    private static IReadOnlyCollection<string> ReadAliases(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (!document.RootElement.TryGetProperty("aliases", out var aliases)
                || aliases.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return aliases
                .EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.String)
                .Select(x => x.GetString())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToArray();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string NormalizeLookupKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character)
                == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToUpperInvariant(character));
            }
        }

        return builder.ToString();
    }
}
