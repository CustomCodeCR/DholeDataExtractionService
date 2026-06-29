using CustomCodeFramework.Persistence.Abstractions;
using Dhole.DataExtraction.Domain.Extraction.Entities;

namespace Dhole.DataExtraction.Application.Abstractions.Repositories;

public interface IExtractionIssueRepository : IRepository<ExtractionIssue, Guid>
{
    Task AddRangeAsync(
        IReadOnlyCollection<ExtractionIssue> issues,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyCollection<ExtractionIssue>> GetByExtractionExecutionIdAsync(
        Guid extractionExecutionId,
        CancellationToken cancellationToken = default
    );
}
