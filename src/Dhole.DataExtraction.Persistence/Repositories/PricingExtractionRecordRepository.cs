using CustomCodeFramework.Postgres.EntityFramework.Repositories;
using Dhole.DataExtraction.Application.Abstractions.Repositories;
using Dhole.DataExtraction.Contracts.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Entities;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Persistence.Repositories;

public sealed class PricingExtractionRecordRepository(ServiceDbContext dbContext)
    : EfRepository<PricingExtractionRecord, Guid>(dbContext),
        IPricingExtractionRecordRepository
{
    public async Task AddRangeAsync(
        IReadOnlyCollection<PricingExtractionRecord> records,
        CancellationToken cancellationToken = default
    )
    {
        await dbContext.PricingExtractionRecords.AddRangeAsync(records, cancellationToken);
    }

    public async Task<IReadOnlyCollection<PricingExtractionRecord>> GetByExtractionExecutionIdAsync(
        Guid extractionExecutionId,
        CancellationToken cancellationToken = default
    )
    {
        return await dbContext
            .PricingExtractionRecords.AsNoTracking()
            .Where(x => x.ExtractionExecutionId == extractionExecutionId && !x.IsDeleted)
            .OrderBy(x => x.SourceSheetName)
            .ThenBy(x => x.SourceRowNumber)
            .ToListAsync(cancellationToken);
    }

    public async Task<
        IReadOnlyCollection<ExtractedPricingRowDto>
    > GetDtosByExtractionExecutionIdAsync(
        Guid extractionExecutionId,
        CancellationToken cancellationToken = default
    )
    {
        return await dbContext
            .PricingExtractionRecords.AsNoTracking()
            .Where(x => x.ExtractionExecutionId == extractionExecutionId && !x.IsDeleted)
            .OrderBy(x => x.SourceSheetName)
            .ThenBy(x => x.SourceRowNumber)
            .Select(x => new ExtractedPricingRowDto(
                x.Id,
                x.ExtractionExecutionId,
                x.SourceDocumentId,
                x.SourceSheetName,
                x.SourceRowNumber,
                x.OriginPort,
                x.PortOfExit,
                x.DestinationPort,
                x.ContainerType,
                x.Carrier,
                x.Agent,
                x.Commodity,
                x.Currency,
                x.FreeDays,
                x.TransitDays,
                x.ValidFrom,
                x.ValidTo,
                x.OceanFreight,
                x.OriginCharges,
                x.DestinationCharges,
                x.Surcharges,
                x.TotalCost,
                x.TotalSale,
                x.Profit,
                x.Margin,
                x.SpaceComment,
                x.Remarks,
                x.Status.ToString(),
                x.RawJson
            ))
            .ToListAsync(cancellationToken);
    }

    public Task<int> CountByExtractionExecutionIdAsync(
        Guid extractionExecutionId,
        CancellationToken cancellationToken = default
    )
    {
        return dbContext.PricingExtractionRecords.CountAsync(
            x => x.ExtractionExecutionId == extractionExecutionId && !x.IsDeleted,
            cancellationToken
        );
    }

    public async Task DeleteByExtractionExecutionIdAsync(
        Guid extractionExecutionId,
        CancellationToken cancellationToken = default
    )
    {
        var records = await dbContext
            .PricingExtractionRecords.Where(x =>
                x.ExtractionExecutionId == extractionExecutionId && !x.IsDeleted
            )
            .ToListAsync(cancellationToken);

        dbContext.PricingExtractionRecords.RemoveRange(records);
    }
}
