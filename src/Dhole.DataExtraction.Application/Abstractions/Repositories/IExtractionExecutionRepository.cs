using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Persistence.Abstractions;
using Dhole.DataExtraction.Domain.Extraction.Entities;

namespace Dhole.DataExtraction.Application.Abstractions.Repositories;

public interface IExtractionExecutionRepository : IRepository<ExtractionExecution, Guid>
{
    Task<ExtractionExecution?> GetByPricingImportIdAsync(
        Guid pricingImportId,
        CancellationToken cancellationToken = default
    );

    Task<ExtractionExecution?> GetByCorrelationIdAsync(
        string correlationId,
        CancellationToken cancellationToken = default
    );

    Task<bool> ExistsByPricingImportIdAsync(
        Guid pricingImportId,
        CancellationToken cancellationToken = default
    );

    Task<PagedResult<ExtractionExecution>> GetPagedAsync(
        PageRequest page,
        string? search = null,
        CancellationToken cancellationToken = default
    );
}
