namespace Dhole.DataExtraction.Infrastructure.Storage;

public interface IStorageServiceClient
{
    Task<string> UploadAsync(
        string sourceService,
        string entityType,
        Guid entityId,
        string fileName,
        string contentType,
        byte[] content,
        object? metadata,
        CancellationToken cancellationToken = default
    );

    Task<byte[]> DownloadAsync(
        string storageReference,
        CancellationToken cancellationToken = default
    );
}
