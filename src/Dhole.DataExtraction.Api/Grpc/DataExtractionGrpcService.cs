using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.DataExtraction.Application.Extraction.ExtractPricingData;
using Dhole.DataExtraction.Contracts.Extraction;
using Dhole.DataExtraction.Contracts.Grpc;
using Google.Protobuf;
using Grpc.Core;

namespace Dhole.DataExtraction.Api.Grpc;

public sealed class DataExtractionGrpcService(
    ICommandDispatcher commandDispatcher,
    ILogger<DataExtractionGrpcService> logger
) : DataExtractionGrpc.DataExtractionGrpcBase
{
    public override async Task<ExtractFclPricingDataGrpcResponse> ExtractFclPricingData(
        ExtractFclPricingDataGrpcRequest request,
        ServerCallContext context
    )
    {
        try
        {
            if (!Guid.TryParse(request.PricingImportId, out var pricingImportId))
            {
                return Failure(request, "DataExtraction.InvalidPricingImportId", "El id de importación de Pricing no es válido.");
            }

            if (request.FileContent.Length == 0)
            {
                return Failure(request, "DataExtraction.EmptyFile", "Debe enviar el contenido del archivo para ejecutar la extracción.");
            }

            Guid? requestedBy = null;
            if (!string.IsNullOrWhiteSpace(request.RequestedBy) && Guid.TryParse(request.RequestedBy, out var parsedRequestedBy))
            {
                requestedBy = parsedRequestedBy;
            }

            var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
                ? Guid.NewGuid().ToString()
                : request.CorrelationId.Trim();

            logger.LogInformation(
                "Starting FCL pricing extraction by gRPC. PricingImportId: {PricingImportId}, File: {FileName}, Size: {FileSizeBytes}, CorrelationId: {CorrelationId}.",
                pricingImportId,
                request.OriginalFileName,
                request.FileSizeBytes,
                correlationId
            );

            var command = new ExtractPricingDataCommand(
                new ExtractionDataRequest(
                    pricingImportId,
                    correlationId,
                    request.OriginalFileName,
                    string.IsNullOrWhiteSpace(request.ContentType) ? null : request.ContentType,
                    string.IsNullOrWhiteSpace(request.FileExtension) ? null : request.FileExtension,
                    request.FileSizeBytes,
                    request.FileHash,
                    string.IsNullOrWhiteSpace(request.ProfileCode) ? null : request.ProfileCode,
                    requestedBy,
                    string.IsNullOrWhiteSpace(request.RequestedByName) ? null : request.RequestedByName,
                    request.FileContent.ToByteArray()
                )
            );

            var result = await commandDispatcher.DispatchAsync(command, context.CancellationToken);
            if (!result.IsSuccess)
            {
                logger.LogWarning(
                    "FCL pricing extraction failed. PricingImportId: {PricingImportId}, Code: {ErrorCode}, Message: {ErrorMessage}.",
                    pricingImportId,
                    result.Error.Code,
                    result.Error.Message
                );

                return Failure(request, result.Error.Code, result.Error.Message);
            }

            logger.LogInformation(
                "FCL pricing extraction completed. PricingImportId: {PricingImportId}, ExtractionExecutionId: {ExtractionExecutionId}, Rows: {Rows}, Issues: {Issues}.",
                pricingImportId,
                result.Value.ExtractionExecutionId,
                result.Value.Rows.Count,
                result.Value.Issues.Count
            );

            return ToGrpcResponse(result.Value);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(
                "FCL pricing extraction gRPC request was cancelled. PricingImportId: {PricingImportId}.",
                request.PricingImportId
            );

            return Failure(request, "DataExtraction.RequestCancelled", "La solicitud de extracción fue cancelada antes de terminar.");
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Unexpected gRPC failure while extracting FCL pricing data. PricingImportId: {PricingImportId}, File: {FileName}.",
                request.PricingImportId,
                request.OriginalFileName
            );

            return Failure(
                request,
                "DataExtraction.GrpcUnhandledError",
                $"Error inesperado en DataExtraction gRPC: {exception.Message}"
            );
        }
    }

    private static ExtractFclPricingDataGrpcResponse ToGrpcResponse(ExtractPricingDataResponse response)
    {
        var grpcResponse = new ExtractFclPricingDataGrpcResponse
        {
            Success = response.Success,
            ExtractionExecutionId = response.ExtractionExecutionId?.ToString() ?? string.Empty,
            PricingImportId = response.PricingImportId.ToString(),
            CorrelationId = response.CorrelationId,
            Summary = new ExtractionSummaryGrpcModel
            {
                TotalRows = response.Summary.TotalRows,
                ValidRows = response.Summary.ValidRows,
                WarningRows = response.Summary.WarningRows,
                InvalidRows = response.Summary.InvalidRows,
                HasIssues = response.Summary.HasIssues,
            },
            ErrorCode = response.ErrorCode ?? string.Empty,
            ErrorMessage = response.ErrorMessage ?? string.Empty,
        };

        if (response.SourceDocument is not null)
        {
            grpcResponse.SourceDocument = new SourceDocumentGrpcModel
            {
                Id = response.SourceDocument.Id.ToString(),
                OriginalFileName = response.SourceDocument.OriginalFileName,
                ContentType = response.SourceDocument.ContentType ?? string.Empty,
                FileExtension = response.SourceDocument.FileExtension ?? string.Empty,
                FileSizeBytes = response.SourceDocument.FileSizeBytes,
                FileHash = response.SourceDocument.FileHash,
                SourceFileType = response.SourceDocument.SourceFileType,
            };
        }

        if (response.ProfileReference is not null)
        {
            grpcResponse.ProfileReference = ToGrpcReference(response.ProfileReference);
        }

        grpcResponse.Records.AddRange(response.Rows.Select(ToGrpcRecord));
        grpcResponse.Issues.AddRange(response.Issues.Select(ToGrpcIssue));

        return grpcResponse;
    }

    private static PricingExtractionRecordGrpcModel ToGrpcRecord(ExtractedPricingRowDto row)
    {
        var result = new PricingExtractionRecordGrpcModel
        {
            Id = row.Id.ToString(),
            SourceSheetName = row.SourceSheetName ?? string.Empty,
            SourceRowNumber = row.SourceRowNumber ?? 0,
            OriginPort = row.OriginPort ?? string.Empty,
            PortOfExit = row.PortOfExit ?? string.Empty,
            DestinationPort = row.DestinationPort ?? string.Empty,
            ContainerType = row.ContainerType ?? string.Empty,
            Carrier = row.Carrier ?? string.Empty,
            Agent = row.Agent ?? string.Empty,
            Commodity = row.Commodity ?? string.Empty,
            Currency = row.Currency ?? string.Empty,
            ValidFrom = row.ValidFrom?.ToString("O") ?? string.Empty,
            ValidTo = row.ValidTo?.ToString("O") ?? string.Empty,
            SpaceComment = row.SpaceComment ?? string.Empty,
            Remarks = row.Remarks ?? string.Empty,
            Status = row.Status,
            RawJson = row.RawJson ?? string.Empty,
            FreeDays = row.FreeDays ?? 0,
            TransitDays = row.TransitDays ?? 0,
        };

        SetAmounts(result, row);

        SetReferences(result, row);
        return result;
    }

    private static void SetAmounts(
        PricingExtractionRecordGrpcModel result,
        ExtractedPricingRowDto row
    )
    {
        if (row.OceanFreight is not null)
            result.OceanFreight = decimal.ToDouble(row.OceanFreight.Value);
        if (row.OriginCharges is not null)
            result.OriginCharges = decimal.ToDouble(row.OriginCharges.Value);
        if (row.DestinationCharges is not null)
            result.DestinationCharges = decimal.ToDouble(row.DestinationCharges.Value);
        if (row.Surcharges is not null)
            result.Surcharges = decimal.ToDouble(row.Surcharges.Value);
        if (row.TotalCost is not null)
            result.TotalCost = decimal.ToDouble(row.TotalCost.Value);
        if (row.TotalSale is not null)
            result.TotalSale = decimal.ToDouble(row.TotalSale.Value);
        if (row.Profit is not null)
            result.Profit = decimal.ToDouble(row.Profit.Value);
        if (row.Margin is not null)
            result.Margin = decimal.ToDouble(row.Margin.Value);
    }

    private static void SetReferences(
        PricingExtractionRecordGrpcModel result,
        ExtractedPricingRowDto row
    )
    {
        if (row.OriginPortReference is not null)
            result.OriginPortReference = ToGrpcReference(row.OriginPortReference);
        if (row.PortOfExitReference is not null)
            result.PortOfExitReference = ToGrpcReference(row.PortOfExitReference);
        if (row.DestinationPortReference is not null)
            result.DestinationPortReference = ToGrpcReference(row.DestinationPortReference);
        if (row.ContainerTypeReference is not null)
            result.ContainerTypeReference = ToGrpcReference(row.ContainerTypeReference);
        if (row.CarrierReference is not null)
            result.CarrierReference = ToGrpcReference(row.CarrierReference);
        if (row.AgentReference is not null)
            result.AgentReference = ToGrpcReference(row.AgentReference);
        if (row.CurrencyReference is not null)
            result.CurrencyReference = ToGrpcReference(row.CurrencyReference);
    }

    private static CatalogReferenceGrpcModel ToGrpcReference(CatalogReferenceDto reference)
    {
        return new CatalogReferenceGrpcModel
        {
            Resolved = true,
            Id = reference.Id.ToString(),
            CatalogGroupSlug = reference.CatalogGroupSlug,
            Code = reference.Code,
            Slug = reference.Slug,
            Name = reference.Name,
            RawValue = reference.RawValue ?? string.Empty,
        };
    }

    private static ExtractionIssueGrpcModel ToGrpcIssue(ExtractionIssueDto issue)
    {
        return new ExtractionIssueGrpcModel
        {
            Id = issue.Id.ToString(),
            PricingExtractionRecordId = issue.ExtractedPricingRowId?.ToString() ?? string.Empty,
            Code = issue.Code,
            Message = issue.Message,
            IsBlocking = issue.IsBlocking,
            SourceSheetName = issue.SourceSheetName ?? string.Empty,
            SourceRowNumber = issue.SourceRowNumber ?? 0,
            ColumnName = issue.ColumnName ?? string.Empty,
            RawValue = issue.RawValue ?? string.Empty,
        };
    }

    private static ExtractFclPricingDataGrpcResponse Failure(
        ExtractFclPricingDataGrpcRequest request,
        string errorCode,
        string errorMessage
    )
    {
        return new ExtractFclPricingDataGrpcResponse
        {
            Success = false,
            PricingImportId = request.PricingImportId,
            CorrelationId = request.CorrelationId,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Summary = new ExtractionSummaryGrpcModel { HasIssues = true },
        };
    }

}
