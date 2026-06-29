using CustomCodeFramework.Persistence.Abstractions;
using Dhole.DataExtraction.Application.Abstractions.Auditing;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Application.Abstractions.Files;
using Dhole.DataExtraction.Application.Abstractions.Messaging;
using Dhole.DataExtraction.Application.Abstractions.Repositories;
using Dhole.DataExtraction.Application.Auditing;
using Dhole.DataExtraction.Contracts.Events;
using Dhole.DataExtraction.Contracts.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Entities;

namespace Dhole.DataExtraction.Infrastructure.Pipeline;

public sealed class ExtractionPipeline(
    IExtractionFileReader fileReader,
    IDocumentExtractorFactory extractorFactory,
    IColumnMappingService columnMappingService,
    IPricingRecordNormalizer normalizer,
    IDataQualityValidator validator,
    IExtractionExecutionRepository executions,
    ISourceDocumentRepository sourceDocuments,
    IPricingExtractionRecordRepository records,
    IExtractionIssueRepository issues,
    IDataExtractionAuditService audit,
    IIntegrationEventOutboxWriter outbox,
    IUnitOfWork unitOfWork
) : IExtractionPipeline
{
    public async Task<ExtractPricingDataResponse> ExtractPricingDataAsync(
        ExtractionDataRequest request,
        CancellationToken cancellationToken = default
    )
    {
        ExtractionExecution? execution = null;

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
                    "El tipo de archivo no es soportado. Solo se permite PDF, Excel o CSV."
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
                request.ProfileCode,
                request.RequestedBy,
                request.RequestedByName
            );

            execution.Start(request.RequestedBy);
            await executions.AddAsync(execution, cancellationToken);

            var sourceDocument = SourceDocument.Create(
                execution.Id,
                file.OriginalFileName,
                file.ContentType,
                file.FileExtension,
                file.FileSizeBytes,
                file.FileHash,
                file.SourceFileType,
                null,
                request.RequestedBy
            );

            await sourceDocuments.AddAsync(sourceDocument, cancellationToken);

            await audit.PublishAsync(
                new DataExtractionAuditEvent(
                    EventType: DataExtractionAuditEventTypes.ExtractionExecutionStarted,
                    Action: DataExtractionAuditActions.Started,
                    EntityType: DataExtractionAuditEntityTypes.ExtractionExecution,
                    EntityId: execution.Id,
                    ActorUserId: request.RequestedBy,
                    ActorUserName: request.RequestedByName,
                    After: ExtractionExecutionAuditSnapshot.From(execution),
                    Payload: new
                    {
                        request.PricingImportId,
                        request.CorrelationId,
                        file.OriginalFileName,
                        file.FileHash,
                        sourceFileType = file.SourceFileType.ToString(),
                    }
                ),
                cancellationToken
            );

            var extractor = extractorFactory.GetExtractor(file.SourceFileType);
            var document = await extractor.ExtractAsync(
                new DocumentExtractionInput(
                    file.OriginalFileName,
                    file.ContentType,
                    file.FileExtension,
                    file.FileContent,
                    request.ProfileCode
                ),
                cancellationToken
            );

            var mappedRows = await columnMappingService.MapAsync(
                document,
                request.ProfileCode,
                cancellationToken
            );

            var normalizedRecords = await normalizer.NormalizeAsync(
                execution.Id,
                sourceDocument.Id,
                mappedRows,
                request.RequestedBy,
                cancellationToken
            );

            var validation = await validator.ValidateAsync(
                execution.Id,
                normalizedRecords,
                cancellationToken
            );

            await records.AddRangeAsync(normalizedRecords, cancellationToken);
            await issues.AddRangeAsync(validation.Issues, cancellationToken);

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

            await audit.PublishAsync(
                new DataExtractionAuditEvent(
                    EventType: DataExtractionAuditEventTypes.ExtractionExecutionCompleted,
                    Action: DataExtractionAuditActions.Completed,
                    EntityType: DataExtractionAuditEntityTypes.ExtractionExecution,
                    EntityId: execution.Id,
                    ActorUserId: request.RequestedBy,
                    ActorUserName: request.RequestedByName,
                    After: ExtractionExecutionAuditSnapshot.From(execution),
                    Payload: summary
                ),
                cancellationToken
            );

            await outbox.WriteAsync(
                typeof(DataExtractionCompletedIntegrationEvent).FullName!,
                "data-extraction.execution.completed",
                new DataExtractionCompletedIntegrationEvent(
                    execution.Id,
                    execution.PricingImportId,
                    execution.CorrelationId,
                    execution.OriginalFileName,
                    execution.FileHash,
                    execution.SourceFileType.ToString(),
                    summary.TotalRows,
                    summary.ValidRows,
                    summary.WarningRows,
                    summary.InvalidRows,
                    summary.HasIssues,
                    DateTime.UtcNow
                ),
                execution.CorrelationId,
                cancellationToken
            );

            await unitOfWork.SaveChangesAsync(cancellationToken);

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
                null
            );
        }
        catch (Exception exception)
        {
            if (execution is not null)
            {
                execution.Fail(exception.Message, request.RequestedBy);

                await audit.PublishAsync(
                    new DataExtractionAuditEvent(
                        EventType: DataExtractionAuditEventTypes.ExtractionExecutionFailed,
                        Action: DataExtractionAuditActions.Failed,
                        EntityType: DataExtractionAuditEntityTypes.ExtractionExecution,
                        EntityId: execution.Id,
                        ActorUserId: request.RequestedBy,
                        ActorUserName: request.RequestedByName,
                        ErrorMessage: exception.Message,
                        After: ExtractionExecutionAuditSnapshot.From(execution)
                    ),
                    cancellationToken
                );

                await outbox.WriteAsync(
                    typeof(DataExtractionFailedIntegrationEvent).FullName!,
                    "data-extraction.execution.failed",
                    new DataExtractionFailedIntegrationEvent(
                        execution.Id,
                        execution.PricingImportId,
                        execution.CorrelationId,
                        execution.OriginalFileName,
                        execution.FileHash,
                        execution.SourceFileType.ToString(),
                        "DataExtraction.ExtractionFailed",
                        exception.Message,
                        DateTime.UtcNow
                    ),
                    execution.CorrelationId,
                    cancellationToken
                );

                await unitOfWork.SaveChangesAsync(cancellationToken);
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
