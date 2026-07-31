using Dhole.DataExtraction.Application.Abstractions.Files;
using Dhole.DataExtraction.Infrastructure.Storage;

namespace Dhole.DataExtraction.Infrastructure.Files;

public sealed class StorageServiceExtractionSourceFileStorage(
    IStorageServiceClient storageClient
) : IExtractionSourceFileStorage
{
    public Task<string> SaveAsync(
        Guid extractionExecutionId,
        string originalFileName,
        byte[] content,
        CancellationToken cancellationToken = default
    )
    {
        return storageClient.UploadAsync(
            "DholeDataExtractionService",
            "ExtractionExecutionSource",
            extractionExecutionId,
            originalFileName,
            ResolveContentType(originalFileName),
            content,
            new
            {
                Kind = "PricingImportSource",
                ExtractionExecutionId = extractionExecutionId,
                OriginalFileName = originalFileName,
            },
            cancellationToken
        );
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
