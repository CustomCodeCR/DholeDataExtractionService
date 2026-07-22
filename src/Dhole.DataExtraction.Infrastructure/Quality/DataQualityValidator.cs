using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Entities;
using Dhole.DataExtraction.Domain.Extraction.ValueObjects;

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
        AddMissingReviewIssue(issues, extractionExecutionId, record, record.PortOfExit, "missing_port_of_exit", "La fila no tiene puerto de salida ni puerto de destino disponible para usar como POE.", "PortOfExit");
        AddRequiredIssue(issues, extractionExecutionId, record, record.DestinationPort, "missing_destination_port", "La fila no tiene puerto de destino.", "DestinationPort");
        AddRequiredIssue(issues, extractionExecutionId, record, record.ContainerType, "missing_container_type", "La fila no tiene tipo de contenedor.", "ContainerType");
        AddRequiredIssue(issues, extractionExecutionId, record, record.Carrier, "missing_carrier", "La fila no tiene naviera.", "Carrier");
        AddMissingReviewIssue(issues, extractionExecutionId, record, record.Agent, "missing_agent", "La fila no tiene agente y quedará pendiente de asignación en Pricing.", "Agent");
        AddRequiredIssue(issues, extractionExecutionId, record, record.Currency, "missing_currency", "La fila no tiene moneda.", "Currency");

        if (record.ValidFrom is null)
        {
            issues.Add(CreateIssue(extractionExecutionId, record, "missing_valid_from", "La fila no tiene fecha inicial de vigencia.", true, "ValidFrom"));
        }

        if (record.ValidTo is null)
        {
            issues.Add(CreateIssue(extractionExecutionId, record, "missing_valid_to", "La fila no tiene fecha final de vigencia.", true, "ValidTo"));
        }

        if (record.ValidFrom is not null
            && record.ValidTo is not null
            && record.ValidTo.Value < record.ValidFrom.Value)
        {
            issues.Add(CreateIssue(extractionExecutionId, record, "invalid_validity_range", "La fecha final de vigencia es menor que la fecha inicial.", true, "ValidTo"));
        }

        AddCatalogReferenceIssue(issues, extractionExecutionId, record, record.OriginPort, record.OriginPortReference, "unknown_origin_port", "El POL no coincide con un elemento activo de Config; la fila no se enviará a Pricing.", "OriginPort", true);
        AddCatalogReferenceIssue(issues, extractionExecutionId, record, record.PortOfExit, record.PortOfExitReference, "unknown_port_of_exit", "El POE no coincide con un elemento activo de Config; la fila no se enviará a Pricing.", "PortOfExit", true);
        AddCatalogReferenceIssue(issues, extractionExecutionId, record, record.DestinationPort, record.DestinationPortReference, "unknown_destination_port", "El POD no coincide con un elemento activo de Config; la fila no se enviará a Pricing.", "DestinationPort", true);
        AddCatalogReferenceIssue(issues, extractionExecutionId, record, record.ContainerType, record.ContainerTypeReference, "unknown_container_type", "El tipo de contenedor no coincide con un elemento activo de Config; la fila no se enviará a Pricing.", "ContainerType", true);
        AddCatalogReferenceIssue(issues, extractionExecutionId, record, record.Carrier, record.CarrierReference, "unknown_carrier", "La naviera no coincide con un elemento activo de Config; la fila no se enviará a Pricing.", "Carrier", true);
        AddCatalogReferenceIssue(issues, extractionExecutionId, record, record.Agent, record.AgentReference, "unknown_agent", "El agente no coincide con Config y quedará pendiente de asignación en Pricing.", "Agent", false);
        AddCatalogReferenceIssue(issues, extractionExecutionId, record, record.Currency, record.CurrencyReference, "unknown_currency", "La moneda no coincide con un elemento activo de Config; la fila no se enviará a Pricing.", "Currency", true);

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

    private static void AddMissingReviewIssue(
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

        issues.Add(CreateIssue(extractionExecutionId, record, code, message, false, columnName));
    }

    private static ExtractionIssue CreateIssue(
        Guid extractionExecutionId,
        PricingExtractionRecord record,
        string code,
        string message,
        bool isBlocking,
        string columnName,
        string? rawValue = null
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
            rawValue,
            null
        );
    }

    private static void AddCatalogReferenceIssue(
        List<ExtractionIssue> issues,
        Guid extractionExecutionId,
        PricingExtractionRecord record,
        string? rawValue,
        CatalogItemReference? reference,
        string code,
        string message,
        string columnName,
        bool isBlocking
    )
    {
        if (string.IsNullOrWhiteSpace(rawValue) || reference is not null)
        {
            return;
        }

        issues.Add(
            CreateIssue(
                extractionExecutionId,
                record,
                code,
                message,
                isBlocking,
                columnName,
                rawValue
            )
        );
    }
}
