using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Dhole.DataExtraction.Infrastructure.Storage;

internal sealed class StorageServiceClient(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<StorageServiceClient> logger
) : IStorageServiceClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly Guid? _providerId = ParseProviderId(
        configuration["StorageService:ProviderId"]
    );

    public async Task<string> UploadAsync(
        string sourceService,
        string entityType,
        Guid entityId,
        string fileName,
        string contentType,
        byte[] content,
        object? metadata,
        CancellationToken cancellationToken = default
    )
    {
        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(content);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(
            string.IsNullOrWhiteSpace(contentType)
                ? "application/octet-stream"
                : contentType
        );

        form.Add(fileContent, "file", SafeFileName(fileName));
        form.Add(new StringContent(sourceService), "sourceService");
        form.Add(new StringContent(entityType), "entityType");
        form.Add(new StringContent(entityId.ToString("D")), "entityId");

        if (_providerId.HasValue)
        {
            form.Add(new StringContent(_providerId.Value.ToString("D")), "providerId");
        }

        if (metadata is not null)
        {
            form.Add(
                new StringContent(JsonSerializer.Serialize(metadata, JsonOptions)),
                "metadataJson"
            );
        }

        using var response = await httpClient.PostAsync(
            "api/v1/storage/files",
            form,
            cancellationToken
        );

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Storage rechazó la carga '{fileName}' con HTTP {(int)response.StatusCode}: {Trim(error, 2000)}"
            );
        }

        var result = await response.Content.ReadFromJsonAsync<StorageUploadResponse>(
            JsonOptions,
            cancellationToken
        );

        if (result is null || string.IsNullOrWhiteSpace(result.Reference))
        {
            throw new InvalidOperationException(
                "Storage completó la carga, pero no devolvió una referencia de archivo."
            );
        }

        logger.LogInformation(
            "Stored {EntityType} {EntityId} as {StorageReference} ({FileName}, {Size} bytes).",
            entityType,
            entityId,
            result.Reference,
            fileName,
            content.LongLength
        );

        return result.Reference;
    }

    public async Task<byte[]> DownloadAsync(
        string storageReference,
        CancellationToken cancellationToken = default
    )
    {
        var fileId = ParseStorageReference(storageReference);
        using var response = await httpClient.GetAsync(
            $"api/v1/storage/files/{fileId:D}/content",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Storage no pudo descargar '{storageReference}' (HTTP {(int)response.StatusCode}): {Trim(error, 2000)}"
            );
        }

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private static Guid ParseStorageReference(string storageReference)
    {
        if (string.IsNullOrWhiteSpace(storageReference))
        {
            throw new InvalidOperationException("La referencia de Storage está vacía.");
        }

        var value = storageReference.Trim();
        if (value.StartsWith("storage://", StringComparison.OrdinalIgnoreCase))
        {
            value = value["storage://".Length..];
        }

        value = value.Trim('/');
        if (!Guid.TryParse(value, out var fileId))
        {
            throw new InvalidOperationException(
                $"La referencia '{storageReference}' no tiene el formato storage://GUID."
            );
        }

        return fileId;
    }

    private static Guid? ParseProviderId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Guid.TryParse(value, out var providerId)
            ? providerId
            : throw new InvalidOperationException(
                "StorageService:ProviderId debe contener un GUID válido."
            );
    }

    private static string SafeFileName(string fileName)
    {
        var value = Path.GetFileName(
            string.IsNullOrWhiteSpace(fileName) ? "source.bin" : fileName.Trim()
        );
        return string.IsNullOrWhiteSpace(value) ? "source.bin" : value;
    }

    private static string Trim(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private sealed record StorageUploadResponse(Guid Id, string Reference);
}
