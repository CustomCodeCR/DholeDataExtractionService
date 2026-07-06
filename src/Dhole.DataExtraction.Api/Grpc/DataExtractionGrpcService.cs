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

        grpcResponse.Records.AddRange(response.Rows.Select(ToGrpcRecord));
        grpcResponse.Issues.AddRange(response.Issues.Select(ToGrpcIssue));

        return grpcResponse;
    }

    private static PricingExtractionRecordGrpcModel ToGrpcRecord(ExtractedPricingRowDto row)
    {
        return new PricingExtractionRecordGrpcModel
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
            OceanFreight = ToDouble(row.OceanFreight),
            OriginCharges = ToDouble(row.OriginCharges),
            DestinationCharges = ToDouble(row.DestinationCharges),
            Surcharges = ToDouble(row.Surcharges),
            TotalCost = ToDouble(row.TotalCost),
            TotalSale = ToDouble(row.TotalSale),
            Profit = ToDouble(row.Profit),
            Margin = ToDouble(row.Margin),
            SpaceComment = row.SpaceComment ?? string.Empty,
            Remarks = row.Remarks ?? string.Empty,
            Status = row.Status,
            RawJson = row.RawJson ?? string.Empty,
            FreeDays = row.FreeDays ?? 0,
            TransitDays = row.TransitDays ?? 0,
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

    private static double ToDouble(decimal? value) => value.HasValue ? decimal.ToDouble(value.Value) : 0;
}
