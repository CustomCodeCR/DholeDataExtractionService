using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Application.Abstractions.Files;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Application.Extraction;
using Dhole.DataExtraction.Contracts.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Entities;
using Dhole.DataExtraction.Domain.Extraction.ValueObjects;

namespace Dhole.DataExtraction.Infrastructure.Pipeline;

public sealed class ExtractionPipeline(
    IExtractionFileReader fileReader,
    IDocumentExtractorFactory extractorFactory,
    IColumnMappingService columnMappingService,
    IPricingRecordNormalizer normalizer,
    IPricingCatalogStandardizer catalogStandardizer,
    IDataQualityValidator validator,
    IConfigCatalogClient configCatalogClient
) : IExtractionPipeline
{
    public async Task<ExtractPricingDataResponse> ExtractPricingDataAsync(
        ExtractionDataRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ExtractionExecution? execution = null;
        CatalogReferenceDto? profileReference = null;

        try
        {
            var file = await fileReader.ReadAsync(
                request.OriginalFileName,
                request.ContentType,
                request.FileContent,
                cancellationToken
            );

            if (!extractorFactory.CanExtract(file.SourceFileType))
            {
                return Failure(
                    request,
                    null,
                    "DataExtraction.UnsupportedFileType",
                    "El tipo de archivo no es soportado. Se permite PDF, Excel, CSV, imagen o correo/HTML. Las imágenes requieren AI con capacidad Vision."
                );
            }

            var mappingProfileCode = request.ProfileCode;
            if (!string.IsNullOrWhiteSpace(request.ProfileCode))
            {
                var profileItem = await configCatalogClient.ResolveCatalogItemAsync(
                    PricingCatalogSlugs.ImportProfiles,
                    request.ProfileCode,
                    cancellationToken
                );

                if (profileItem is null)
                {
                    throw new InvalidOperationException(
                        $"El perfil '{request.ProfileCode}' no existe o está inactivo en el catálogo '{PricingCatalogSlugs.ImportProfiles}'."
                    );
                }

                mappingProfileCode = profileItem.Value ?? profileItem.Code;
                profileReference = new CatalogReferenceDto(
                    profileItem.Id,
                    profileItem.CatalogGroupSlug,
                    profileItem.Code,
                    profileItem.Slug,
                    profileItem.Name,
                    request.ProfileCode
                );
            }

            execution = ExtractionExecution.Create(
                request.PricingImportId,
                request.CorrelationId,
                file.OriginalFileName,
                file.ContentType,
                file.FileExtension,
                file.FileSizeBytes,
                file.FileHash,
                file.SourceFileType,
                mappingProfileCode,
                request.RequestedBy,
                request.RequestedByName
            );

            execution.SetSourceOrigin(
                request.SourceOriginType,
                request.SourceOriginId,
                request.SourceEmailMessageId,
                request.SourceEmailAttachmentId
            );

            execution.Start(request.RequestedBy);

            // DataExtraction never persists source binaries. StoragePath is an opaque
            // reference supplied by DholeStorageService (or by the caller once Storage
            // has uploaded the file). Direct uploads are processed only in memory.
            var storagePath = string.IsNullOrWhiteSpace(request.StoragePath)
                ? null
                : request.StoragePath.Trim();

            var sourceDocument = SourceDocument.Create(
                execution.Id,
                file.OriginalFileName,
                file.ContentType,
                file.FileExtension,
                file.FileSizeBytes,
                file.FileHash,
                file.SourceFileType,
                storagePath,
                request.RequestedBy
            );


            var extractor = extractorFactory.GetExtractor(file.SourceFileType);
            var document = await extractor.ExtractAsync(
                new DocumentExtractionInput(
                    file.OriginalFileName,
                    file.ContentType,
                    file.FileExtension,
                    file.FileContent,
                    mappingProfileCode
                ),
                cancellationToken
            );

            var mappedRows = await columnMappingService.MapAsync(
                document,
                mappingProfileCode,
                cancellationToken
            );

            if (mappedRows.Count == 0)
            {
                throw new InvalidOperationException(
                    "No se encontraron filas de tarifas FCL con columnas reconocibles. Revise que el archivo tenga encabezados como POL, POD, Equipo, Naviera, Flete o Total Venta."
                );
            }

            var normalizedRecords = await normalizer.NormalizeAsync(
                execution.Id,
                sourceDocument.Id,
                mappedRows,
                request.RequestedBy,
                cancellationToken
            );

            if (normalizedRecords.Count == 0)
            {
                throw new InvalidOperationException(
                    "El archivo fue leído, pero no se pudo normalizar ninguna fila de tarifa FCL."
                );
            }

            await catalogStandardizer.StandardizeAsync(
                normalizedRecords,
                request.RequestedBy,
                cancellationToken
            );

            var validation = await validator.ValidateAsync(
                execution.Id,
                normalizedRecords,
                cancellationToken
            );

            execution.Complete(
                validation.TotalRows,
                validation.ValidRows,
                validation.WarningRows,
                validation.InvalidRows,
                request.RequestedBy
            );

            var summary = new ExtractionSummaryDto(
                validation.TotalRows,
                validation.ValidRows,
                validation.WarningRows,
                validation.InvalidRows,
                validation.HasIssues
            );

            var rowDtos = normalizedRecords.Select(ToDto).ToArray();
            var issueDtos = validation.Issues.Select(ToDto).ToArray();
            var sourceDocumentDto = ToDto(sourceDocument);

            return new ExtractPricingDataResponse(
                true,
                execution.Id,
                execution.PricingImportId,
                execution.CorrelationId,
                summary,
                sourceDocumentDto,
                rowDtos,
                issueDtos,
                null,
                null,
                profileReference
            );
        }
        catch (Exception exception)
        {
            if (execution is not null)
            {
                execution.Fail(exception.Message, request.RequestedBy);
            }

            return Failure(
                request,
                execution?.Id,
                "DataExtraction.ExtractionFailed",
                exception.Message
            );
        }
    }

    private static ExtractPricingDataResponse Failure(
        ExtractionDataRequest request,
        Guid? extractionExecutionId,
        string errorCode,
        string errorMessage
    )
    {
        return new ExtractPricingDataResponse(
            false,
            extractionExecutionId,
            request.PricingImportId,
            request.CorrelationId,
            new ExtractionSummaryDto(0, 0, 0, 0, true),
            null,
            Array.Empty<ExtractedPricingRowDto>(),
            Array.Empty<ExtractionIssueDto>(),
            errorCode,
            errorMessage
        );
    }

    private static ExtractionSourceDocumentDto ToDto(SourceDocument document)
    {
        return new ExtractionSourceDocumentDto(
            document.Id,
            document.ExtractionExecutionId,
            document.OriginalFileName,
            document.ContentType,
            document.FileExtension,
            document.FileSizeBytes,
            document.FileHash,
            document.SourceFileType.ToString(),
            document.StoragePath
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
            record.RawJson,
            ToDto(record.OriginPortReference),
            ToDto(record.PortOfExitReference),
            ToDto(record.DestinationPortReference),
            ToDto(record.ContainerTypeReference),
            ToDto(record.CarrierReference),
            ToDto(record.AgentReference),
            ToDto(record.CurrencyReference)
        );
    }

    private static CatalogReferenceDto? ToDto(CatalogItemReference? reference)
    {
        return reference is null
            ? null
            : new CatalogReferenceDto(
                reference.CatalogItemId,
                reference.CatalogGroupSlug,
                reference.Code,
                reference.Slug,
                reference.Name,
                reference.RawValue
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
