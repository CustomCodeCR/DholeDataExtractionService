using Dhole.Config.Contracts.Grpc;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Grpc.Core;

namespace Dhole.DataExtraction.Infrastructure.GrpcClients;

public sealed class ConfigCatalogGrpcClient(
    ConfigCatalogGrpc.ConfigCatalogGrpcClient client
) : IConfigCatalogClient
{
    public async Task<ConfigCatalogItemResult?> ResolveCatalogItemAsync(
        string catalogGroupSlug,
        string value,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var response = await client.ResolveCatalogItemAsync(
                new ResolveCatalogItemGrpcRequest
                {
                    CatalogGroupSlug = catalogGroupSlug,
                    Value = value,
                },
                cancellationToken: cancellationToken
            );

            return !response.Found || response.Item is null || !response.Item.IsActive
                ? null
                : ToResult(response.Item);
        }
        catch (RpcException exception)
        {
            throw CreateUnavailableException(exception);
        }
    }

    public async Task<bool> ValidateCatalogItemAsync(
        string catalogGroupSlug,
        string catalogItemSlug,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var response = await client.ValidateCatalogItemAsync(
                new ValidateCatalogItemGrpcRequest
                {
                    CatalogGroupSlug = catalogGroupSlug,
                    CatalogItemSlug = catalogItemSlug,
                },
                cancellationToken: cancellationToken
            );

            return response.IsValid;
        }
        catch (RpcException exception)
        {
            throw CreateUnavailableException(exception);
        }
    }

    public async Task<IReadOnlyCollection<ConfigCatalogItemResult>> GetActiveCatalogItemsByGroupAsync(
        string catalogGroupSlug,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var response = await client.GetActiveCatalogItemsByGroupAsync(
                new GetActiveCatalogItemsByGroupGrpcRequest
                {
                    CatalogGroupSlug = catalogGroupSlug,
                },
                cancellationToken: cancellationToken
            );

            return response.Items.Select(ToResult).ToArray();
        }
        catch (RpcException exception)
        {
            throw CreateUnavailableException(exception);
        }
    }

    private static ConfigCatalogItemResult ToResult(CatalogItemGrpcModel item)
    {
        if (!Guid.TryParse(item.Id, out var id))
        {
            throw new InvalidOperationException(
                $"Config devolvió un identificador inválido para {item.CatalogGroupSlug}/{item.Slug}."
            );
        }

        return new ConfigCatalogItemResult(
            id,
            item.CatalogGroupSlug,
            item.Code,
            item.Slug,
            item.Name,
            EmptyToNull(item.Value),
            EmptyToNull(item.MetadataJson),
            item.IsActive
        );
    }

    private static InvalidOperationException CreateUnavailableException(RpcException exception)
    {
        return new InvalidOperationException(
            $"Config.{exception.StatusCode}: {exception.Status.Detail}",
            exception
        );
    }

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
