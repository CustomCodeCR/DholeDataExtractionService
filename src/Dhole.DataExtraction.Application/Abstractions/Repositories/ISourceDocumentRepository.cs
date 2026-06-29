using CustomCodeFramework.Persistence.Abstractions;
using Dhole.DataExtraction.Domain.Extraction.Entities;

namespace Dhole.DataExtraction.Application.Abstractions.Repositories;

public interface ISourceDocumentRepository : IRepository<SourceDocument, Guid>
{
    Task<SourceDocument?> GetByExtractionExecutionIdAsync(
        Guid extractionExecutionId,
        CancellationToken cancellationToken = default
    );

    Task<SourceDocument?> GetByFileHashAsync(
        string fileHash,
        CancellationToken cancellationToken = default
    );

    Task<bool> ExistsByFileHashAsync(
        string fileHash,
        CancellationToken cancellationToken = default
    );
}
