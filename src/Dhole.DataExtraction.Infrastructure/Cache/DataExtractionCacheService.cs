using System.Collections.Concurrent;
using Dhole.DataExtraction.Application.Abstractions.Cache;
using Dhole.DataExtraction.Contracts.Extraction;

namespace Dhole.DataExtraction.Infrastructure.Cache;

public sealed class DataExtractionCacheService : IDataExtractionCacheService
{
    private static readonly ConcurrentDictionary<string, ExtractionSourceDocumentDto> Documents = new();
    private static readonly ConcurrentDictionary<Guid, IReadOnlyCollection<ExtractedPricingRowDto>> Rows = new();

    public Task<ExtractionSourceDocumentDto?> GetSourceDocumentByHashAsync(
        string fileHash,
        CancellationToken cancellationToken = default
    )
    {
        Documents.TryGetValue(fileHash, out var value);
        return Task.FromResult(value);
    }

    public Task SetSourceDocumentByHashAsync(
        string fileHash,
        ExtractionSourceDocumentDto sourceDocument,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    )
    {
        Documents[fileHash] = sourceDocument;
        return Task.CompletedTask;
    }

    public Task RemoveSourceDocumentByHashAsync(
        string fileHash,
        CancellationToken cancellationToken = default
    )
    {
        Documents.TryRemove(fileHash, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<ExtractedPricingRowDto>?> GetExtractedRowsAsync(
        Guid extractionExecutionId,
        CancellationToken cancellationToken = default
    )
    {
        Rows.TryGetValue(extractionExecutionId, out var value);
        return Task.FromResult(value);
    }

    public Task SetExtractedRowsAsync(
        Guid extractionExecutionId,
        IReadOnlyCollection<ExtractedPricingRowDto> rows,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    )
    {
        Rows[extractionExecutionId] = rows;
        return Task.CompletedTask;
    }

    public Task RemoveExtractedRowsAsync(
        Guid extractionExecutionId,
        CancellationToken cancellationToken = default
    )
    {
        Rows.TryRemove(extractionExecutionId, out _);
        return Task.CompletedTask;
    }

    public Task RemoveExtractionCacheAsync(
        Guid extractionExecutionId,
        string? fileHash = null,
        CancellationToken cancellationToken = default
    )
    {
        Rows.TryRemove(extractionExecutionId, out _);

        if (!string.IsNullOrWhiteSpace(fileHash))
        {
            Documents.TryRemove(fileHash, out _);
        }

        return Task.CompletedTask;
    }
}
