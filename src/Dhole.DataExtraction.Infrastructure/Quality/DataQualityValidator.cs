using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Entities;

namespace Dhole.DataExtraction.Infrastructure.Quality;

public sealed class DataQualityValidator : IDataQualityValidator
{
    public Task<ExtractionValidationResult> ValidateAsync(
        Guid extractionExecutionId,
        IReadOnlyCollection<PricingExtractionRecord> records,
        CancellationToken cancellationToken = default
    )
    {
        var issues = new List<ExtractionIssue>();
        var validRows = 0;
        var warningRows = 0;
        var invalidRows = 0;

        foreach (var record in records)
        {
            var rowIssues = ValidateRecord(extractionExecutionId, record);

            if (rowIssues.Any(x => x.IsBlocking))
            {
                record.MarkAsInvalid();
                invalidRows++;
            }
            else if (rowIssues.Count > 0)
            {
                record.MarkAsRequiresReview();
                warningRows++;
            }
            else
            {
                record.MarkAsValid();
                validRows++;
            }

            issues.AddRange(rowIssues);
        }

        return Task.FromResult(
            new ExtractionValidationResult(records.Count, validRows, warningRows, invalidRows, issues)
        );
    }

    private static IReadOnlyCollection<ExtractionIssue> ValidateRecord(
        Guid extractionExecutionId,
        PricingExtractionRecord record
    )
    {
        var issues = new List<ExtractionIssue>();

        AddRequiredIssue(issues, extractionExecutionId, record, record.OriginPort, "missing_origin_port", "La fila no tiene puerto de origen.", "OriginPort");
        AddRequiredIssue(issues, extractionExecutionId, record, record.DestinationPort, "missing_destination_port", "La fila no tiene puerto de destino.", "DestinationPort");
        AddRequiredIssue(issues, extractionExecutionId, record, record.ContainerType, "missing_container_type", "La fila no tiene tipo de contenedor.", "ContainerType");
        AddRequiredIssue(issues, extractionExecutionId, record, record.Carrier, "missing_carrier", "La fila no tiene naviera.", "Carrier");

        if (record.TotalSale is null && record.OceanFreight is null)
        {
            issues.Add(CreateIssue(extractionExecutionId, record, "missing_rate_amount", "La fila no tiene monto de tarifa o flete.", true, "TotalSale"));
        }

        if (record.ValidTo is not null && record.ValidTo.Value.Date < DateTime.UtcNow.Date)
        {
            issues.Add(CreateIssue(extractionExecutionId, record, "expired_rate", "La tarifa está vencida.", false, "ValidTo"));
        }

        if (record.Margin is not null && record.Margin < 0)
        {
            issues.Add(CreateIssue(extractionExecutionId, record, "negative_margin", "La fila tiene margen negativo.", false, "Margin"));
        }

        return issues;
    }

    private static void AddRequiredIssue(
        List<ExtractionIssue> issues,
        Guid extractionExecutionId,
        PricingExtractionRecord record,
        string? value,
        string code,
        string message,
        string columnName
    )
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        issues.Add(CreateIssue(extractionExecutionId, record, code, message, true, columnName));
    }

    private static ExtractionIssue CreateIssue(
        Guid extractionExecutionId,
        PricingExtractionRecord record,
        string code,
        string message,
        bool isBlocking,
        string columnName
    )
    {
        return ExtractionIssue.Create(
            extractionExecutionId,
            record.Id,
            code,
            message,
            isBlocking,
            record.SourceSheetName,
            record.SourceRowNumber,
            columnName,
            null,
            null
        );
    }
}
