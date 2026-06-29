using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.DataExtraction.Application.Extraction.ExtractPricingData;
using Dhole.DataExtraction.Contracts.Extraction;
using Dhole.DataExtraction.Contracts.Grpc;
using Google.Protobuf;
using Grpc.Core;

namespace Dhole.DataExtraction.Api.Grpc;

public sealed class DataExtractionGrpcService(ICommandDispatcher commandDispatcher)
    : DataExtractionGrpc.DataExtractionGrpcBase
{
    public override async Task<ExtractFclPricingDataGrpcResponse> ExtractFclPricingData(
        ExtractFclPricingDataGrpcRequest request,
        ServerCallContext context
    )
    {
        if (!Guid.TryParse(request.PricingImportId, out var pricingImportId))
        {
            return Failure(request, "DataExtraction.InvalidPricingImportId", "El id de importación de Pricing no es válido.");
        }

        Guid? requestedBy = null;
        if (!string.IsNullOrWhiteSpace(request.RequestedBy) && Guid.TryParse(request.RequestedBy, out var parsedRequestedBy))
        {
            requestedBy = parsedRequestedBy;
        }

        var command = new ExtractPricingDataCommand(
            new ExtractionDataRequest(
                pricingImportId,
                string.IsNullOrWhiteSpace(request.CorrelationId) ? Guid.NewGuid().ToString() : request.CorrelationId,
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
            return Failure(request, result.Error.Code, result.Error.Message);
        }

        return ToGrpcResponse(result.Value);
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
