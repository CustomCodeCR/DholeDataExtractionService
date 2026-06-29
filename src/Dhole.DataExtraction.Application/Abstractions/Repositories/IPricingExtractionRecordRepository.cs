using CustomCodeFramework.Persistence.Abstractions;
using Dhole.DataExtraction.Contracts.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Entities;

namespace Dhole.DataExtraction.Application.Abstractions.Repositories;

public interface IPricingExtractionRecordRepository : IRepository<PricingExtractionRecord, Guid>
{
    Task AddRangeAsync(
        IReadOnlyCollection<PricingExtractionRecord> records,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyCollection<PricingExtractionRecord>> GetByExtractionExecutionIdAsync(
        Guid extractionExecutionId,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyCollection<ExtractedPricingRowDto>> GetDtosByExtractionExecutionIdAsync(
        Guid extractionExecutionId,
        CancellationToken cancellationToken = default
    );

    Task<int> CountByExtractionExecutionIdAsync(
        Guid extractionExecutionId,
        CancellationToken cancellationToken = default
    );

    Task DeleteByExtractionExecutionIdAsync(
        Guid extractionExecutionId,
        CancellationToken cancellationToken = default
    );
}
