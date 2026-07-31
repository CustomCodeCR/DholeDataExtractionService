using Dhole.DataExtraction.Application.Abstractions.Emails;
using Dhole.DataExtraction.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;

namespace Dhole.DataExtraction.Infrastructure.Email;

public sealed class StorageServiceEmailFileStorage(
    IStorageServiceClient storageClient,
    IConfiguration configuration
) : IEmailFileStorage
{
    private const string SourceService = "DholeDataExtractionService";
    private readonly string _legacyRootPath = ResolveLegacyRootPath(configuration);

    public Task<string> SaveRawEmailAsync(
        Guid emailMessageId,
        byte[] content,
        CancellationToken cancellationToken = default
    )
    {
        return storageClient.UploadAsync(
            SourceService,
            "EmailMessage",
            emailMessageId,
            "raw.eml",
            "message/rfc822",
            content,
            new
            {
                Kind = "RawEmail",
                EmailMessageId = emailMessageId,
            },
            cancellationToken
        );
    }

    public Task<string> SaveAttachmentAsync(
        Guid emailMessageId,
        Guid attachmentId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default
    )
    {
        return storageClient.UploadAsync(
            SourceService,
            "EmailAttachment",
            attachmentId,
            fileName,
            ResolveContentType(fileName),
            content,
            new
            {
                Kind = "EmailAttachment",
                EmailMessageId = emailMessageId,
                AttachmentId = attachmentId,
                OriginalFileName = fileName,
            },
            cancellationToken
        );
    }

    public async Task<byte[]> ReadAsync(
        string storagePath,
        CancellationToken cancellationToken = default
    )
    {
        if (storagePath.StartsWith("storage://", StringComparison.OrdinalIgnoreCase))
        {
            return await storageClient.DownloadAsync(storagePath, cancellationToken);
        }

        // Compatibilidad con mensajes ingeridos antes de activar DholeStorageService.
        var legacyPath = Path.IsPathRooted(storagePath)
            ? storagePath
            : Path.Combine(_legacyRootPath, storagePath.Replace('/', Path.DirectorySeparatorChar));

        if (File.Exists(legacyPath))
        {
            return await File.ReadAllBytesAsync(legacyPath, cancellationToken);
        }

        return await storageClient.DownloadAsync(storagePath, cancellationToken);
    }

    private static string ResolveLegacyRootPath(IConfiguration configuration)
    {
        var configured = configuration["EmailIngestion:StoragePath"];
        return string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "storage", "data-extraction")
            )
            : Path.GetFullPath(configured.Trim());
    }

    private static string ResolveContentType(string fileName)
    {
        return Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xls" => "application/vnd.ms-excel",
            ".csv" => "text/csv",
            ".txt" => "text/plain",
            ".json" => "application/json",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".eml" => "message/rfc822",
            _ => "application/octet-stream",
        };
    }
}
