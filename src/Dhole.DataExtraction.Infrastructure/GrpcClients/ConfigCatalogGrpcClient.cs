using Dhole.DataExtraction.Application.Abstractions.Services;

namespace Dhole.DataExtraction.Infrastructure.GrpcClients;

public sealed class ConfigCatalogGrpcClient : IConfigCatalogClient
{
    public Task<ConfigCatalogItemResult?> ResolveCatalogItemAsync(
        string catalogGroupSlug,
        string value,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult<ConfigCatalogItemResult?>(null);
    }

    public Task<bool> ValidateCatalogItemAsync(
        string catalogGroupSlug,
        string catalogItemSlug,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult(true);
    }

    public Task<IReadOnlyCollection<ConfigCatalogItemResult>> GetActiveCatalogItemsByGroupAsync(
        string catalogGroupSlug,
        CancellationToken cancellationToken = default
    )
    {
        return Task.FromResult<IReadOnlyCollection<ConfigCatalogItemResult>>(
            Array.Empty<ConfigCatalogItemResult>()
        );
    }
}
