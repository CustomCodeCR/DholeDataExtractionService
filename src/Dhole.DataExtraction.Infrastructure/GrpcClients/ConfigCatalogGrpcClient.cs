using System.Collections.Concurrent;
using Dhole.Config.Contracts.Grpc;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Grpc.Core;
using Microsoft.Extensions.Configuration;

namespace Dhole.DataExtraction.Infrastructure.GrpcClients;

public sealed class ConfigCatalogGrpcClient(
    ConfigCatalogGrpc.ConfigCatalogGrpcClient client,
    IConfiguration configuration
) : IConfigCatalogClient
{
    private static readonly ConcurrentDictionary<string, CachedCatalogGroup> GroupCache =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> GroupLocks =
        new(StringComparer.OrdinalIgnoreCase);

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
                deadline: CreateDeadline(),
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
                deadline: CreateDeadline(),
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
        var cacheKey = catalogGroupSlug.Trim().ToLowerInvariant();
        var now = DateTime.UtcNow;

        if (
            GroupCache.TryGetValue(cacheKey, out var cached)
            && cached.ExpiresAtUtc > now
        )
        {
            return cached.Items;
        }

        var gate = GroupLocks.GetOrAdd(cacheKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);

        try
        {
            now = DateTime.UtcNow;
            if (
                GroupCache.TryGetValue(cacheKey, out cached)
                && cached.ExpiresAtUtc > now
            )
            {
                return cached.Items;
            }

            var items = await LoadActiveCatalogItemsByGroupAsync(
                cacheKey,
                cancellationToken
            );
            var cacheMinutes = ReadPositiveInt(
                configuration["Grpc:Clients:Config:CatalogCacheMinutes"],
                10
            );

            GroupCache[cacheKey] = new CachedCatalogGroup(
                items,
                now.AddMinutes(cacheMinutes)
            );

            return items;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<IReadOnlyCollection<ConfigCatalogItemResult>>
        LoadActiveCatalogItemsByGroupAsync(
            string catalogGroupSlug,
            CancellationToken cancellationToken
        )
    {
        try
        {
            var response = await client.GetActiveCatalogItemsByGroupAsync(
                new GetActiveCatalogItemsByGroupGrpcRequest
                {
                    CatalogGroupSlug = catalogGroupSlug,
                },
                deadline: CreateDeadline(),
                cancellationToken: cancellationToken
            );

            return response.Items.Select(ToResult).ToArray();
        }
        catch (RpcException exception)
        {
            throw CreateUnavailableException(exception);
        }
    }

    private DateTime CreateDeadline()
    {
        var timeoutSeconds = ReadPositiveInt(
            configuration["Grpc:Clients:Config:TimeoutSeconds"],
            15
        );
        return DateTime.UtcNow.AddSeconds(timeoutSeconds);
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

    private sealed record CachedCatalogGroup(
        IReadOnlyCollection<ConfigCatalogItemResult> Items,
        DateTime ExpiresAtUtc
    );

    private static string? EmptyToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }
}
