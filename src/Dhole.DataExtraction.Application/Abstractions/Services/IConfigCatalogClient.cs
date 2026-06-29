namespace Dhole.DataExtraction.Application.Abstractions.Services;

public interface IConfigCatalogClient
{
    Task<ConfigCatalogItemResult?> ResolveCatalogItemAsync(
        string catalogGroupSlug,
        string value,
        CancellationToken cancellationToken = default
    );

    Task<bool> ValidateCatalogItemAsync(
        string catalogGroupSlug,
        string catalogItemSlug,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyCollection<ConfigCatalogItemResult>> GetActiveCatalogItemsByGroupAsync(
        string catalogGroupSlug,
        CancellationToken cancellationToken = default
    );
}

public sealed record ConfigCatalogItemResult(
    Guid Id,
    string CatalogGroupSlug,
    string Code,
    string Slug,
    string Name,
    string? Value,
    string? MetadataJson,
    bool IsActive
);
