using Dhole.DataExtraction.Application.Abstractions.Emails;
using Microsoft.Extensions.Configuration;

namespace Dhole.DataExtraction.Infrastructure.Email;

/// <summary>
/// Boundary for DholeStorageService. DataExtraction must never write files to its
/// own filesystem or database. Until Storage exposes its final client contract,
/// this adapter fails fast instead of silently falling back to local storage.
/// </summary>
public sealed class StorageServiceEmailFileStorage(IConfiguration configuration)
    : IEmailFileStorage
{
    private readonly string? _storageAddress = configuration["StorageService:Address"]?.Trim();

    public Task<string> SaveRawEmailAsync(
        Guid emailMessageId,
        byte[] content,
        CancellationToken cancellationToken = default
    ) => throw NotConfigured();

    public Task<string> SaveAttachmentAsync(
        Guid emailMessageId,
        Guid attachmentId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default
    ) => throw NotConfigured();

    public Task<byte[]> ReadAsync(
        string storageReference,
        CancellationToken cancellationToken = default
    ) => throw NotConfigured();

    private InvalidOperationException NotConfigured()
    {
        var address = string.IsNullOrWhiteSpace(_storageAddress)
            ? "StorageService:Address no está configurado"
            : $"el cliente de Storage para '{_storageAddress}' todavía no está implementado";

        return new InvalidOperationException(
            $"DataExtraction no almacena archivos localmente y {address}. "
            + "Mantenga EmailIngestion:Enabled=false hasta integrar DholeStorageService."
        );
    }
}
