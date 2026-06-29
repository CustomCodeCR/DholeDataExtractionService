using Dhole.DataExtraction.Contracts.Extraction;

namespace Dhole.DataExtraction.Application.Abstractions.Cache;

public interface IDataExtractionCacheService
{
    Task<ExtractionSourceDocumentDto?> GetSourceDocumentByHashAsync(
        string fileHash,
        CancellationToken cancellationToken = default
    );

    Task SetSourceDocumentByHashAsync(
        string fileHash,
        ExtractionSourceDocumentDto sourceDocument,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    );

    Task RemoveSourceDocumentByHashAsync(
        string fileHash,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyCollection<ExtractedPricingRowDto>?> GetExtractedRowsAsync(
        Guid extractionExecutionId,
        CancellationToken cancellationToken = default
    );

    Task SetExtractedRowsAsync(
        Guid extractionExecutionId,
        IReadOnlyCollection<ExtractedPricingRowDto> rows,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default
    );

    Task RemoveExtractedRowsAsync(
        Guid extractionExecutionId,
        CancellationToken cancellationToken = default
    );

    Task RemoveExtractionCacheAsync(
        Guid extractionExecutionId,
        string? fileHash = null,
        CancellationToken cancellationToken = default
    );
}
