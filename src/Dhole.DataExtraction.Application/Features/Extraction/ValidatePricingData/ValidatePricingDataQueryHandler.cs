using CustomCodeFramework.Core.Results;
using CustomCodeFramework.Cqrs.Queries;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Application.Abstractions.Files;
using Dhole.DataExtraction.Contracts.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Entities;

namespace Dhole.DataExtraction.Application.Extraction.ValidatePricingData;

public sealed class ValidatePricingDataQueryHandler(
    IExtractionFileReader fileReader,
    IDocumentExtractorFactory extractorFactory,
    IColumnMappingService columnMapping,
    IPricingRecordNormalizer normalizer,
    IDataQualityValidator validator
) : IQueryHandler<ValidatePricingDataQuery, Result<ValidatePricingDataResponse>>
{
    public async Task<Result<ValidatePricingDataResponse>> HandleAsync(
        ValidatePricingDataQuery query,
        CancellationToken cancellationToken = default
    )
    {
        var file = await fileReader.ReadAsync(
            query.OriginalFileName,
            query.ContentType,
            query.FileContent,
            cancellationToken
        );

        if (!extractorFactory.CanExtract(file.SourceFileType))
        {
            return Result.Failure<ValidatePricingDataResponse>(
                new CustomCodeFramework.Core.Results.Error(
                    "DataExtraction.UnsupportedFileType",
                    "El tipo de archivo no es soportado. Se permite PDF, Excel, CSV o correo/HTML."
                )
            );
        }

        var extractor = extractorFactory.GetExtractor(file.SourceFileType);

        var document = await extractor.ExtractAsync(
            new DocumentExtractionInput(
                file.OriginalFileName,
                file.ContentType,
                file.FileExtension,
                file.FileContent,
                query.ProfileCode
            ),
            cancellationToken
        );

        var mappedRows = await columnMapping.MapAsync(
            document,
            query.ProfileCode,
            cancellationToken
        );

        var extractionExecutionId = Guid.NewGuid();
        var sourceDocumentId = Guid.NewGuid();

        var records = await normalizer.NormalizeAsync(
            extractionExecutionId,
            sourceDocumentId,
            mappedRows,
            query.RequestedBy,
            cancellationToken
        );

        var validation = await validator.ValidateAsync(
            extractionExecutionId,
            records,
            cancellationToken
        );

        var summary = new ExtractionSummaryDto(
            validation.TotalRows,
            validation.ValidRows,
            validation.WarningRows,
            validation.InvalidRows,
            validation.HasIssues
        );

        var rowDtos = records.Select(record => ToDto(record)).ToArray();

        var issueDtos = validation.Issues.Select(issue => ToDto(issue)).ToArray();

        return Result.Success(
            new ValidatePricingDataResponse(
                file.OriginalFileName,
                file.SourceFileType.ToString(),
                summary,
                rowDtos,
                issueDtos
            )
        );
    }

    private static ExtractedPricingRowDto ToDto(PricingExtractionRecord record)
    {
        return new ExtractedPricingRowDto(
            record.Id,
            record.ExtractionExecutionId,
            record.SourceDocumentId,
            record.SourceSheetName,
            record.SourceRowNumber,
            record.OriginPort,
            record.PortOfExit,
            record.DestinationPort,
            record.ContainerType,
            record.Carrier,
            record.Agent,
            record.Commodity,
            record.Currency,
            record.FreeDays,
            record.TransitDays,
            record.ValidFrom,
            record.ValidTo,
            record.OceanFreight,
            record.OriginCharges,
            record.DestinationCharges,
            record.Surcharges,
            record.TotalCost,
            record.TotalSale,
            record.Profit,
            record.Margin,
            record.SpaceComment,
            record.Remarks,
            record.Status.ToString(),
            record.RawJson
        );
    }

    private static ExtractionIssueDto ToDto(ExtractionIssue issue)
    {
        return new ExtractionIssueDto(
            issue.Id,
            issue.ExtractionExecutionId,
            issue.PricingExtractionRecordId,
            issue.Code,
            issue.Message,
            issue.IsBlocking,
            issue.SourceSheetName,
            issue.SourceRowNumber,
            issue.ColumnName,
            issue.RawValue
        );
    }
}
