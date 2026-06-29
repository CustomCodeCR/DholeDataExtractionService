using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Entities;

namespace Dhole.DataExtraction.Infrastructure.Normalization;

public sealed class PricingRecordNormalizer : IPricingRecordNormalizer
{
    public Task<IReadOnlyCollection<PricingExtractionRecord>> NormalizeAsync(
        Guid extractionExecutionId,
        Guid sourceDocumentId,
        IReadOnlyCollection<MappedPricingRow> rows,
        Guid? createdBy = null,
        CancellationToken cancellationToken = default
    )
    {
        var records = rows.Select(row =>
                NormalizeRow(extractionExecutionId, sourceDocumentId, row, createdBy)
            )
            .ToArray();

        return Task.FromResult<IReadOnlyCollection<PricingExtractionRecord>>(records);
    }

    private static PricingExtractionRecord NormalizeRow(
        Guid extractionExecutionId,
        Guid sourceDocumentId,
        MappedPricingRow row,
        Guid? createdBy
    )
    {
        row.Values.TryGetValue("OriginPort", out var originPort);
        row.Values.TryGetValue("PortOfExit", out var portOfExit);
        row.Values.TryGetValue("DestinationPort", out var destinationPort);
        row.Values.TryGetValue("ContainerType", out var containerType);
        row.Values.TryGetValue("Carrier", out var carrier);
        row.Values.TryGetValue("Agent", out var agent);
        row.Values.TryGetValue("Commodity", out var commodity);
        row.Values.TryGetValue("Currency", out var currency);

        row.Values.TryGetValue("ValidFrom", out var validFrom);
        row.Values.TryGetValue("ValidTo", out var validTo);

        row.Values.TryGetValue("OceanFreight", out var oceanFreight);
        row.Values.TryGetValue("OriginCharges", out var originCharges);
        row.Values.TryGetValue("DestinationCharges", out var destinationCharges);
        row.Values.TryGetValue("Surcharges", out var surcharges);
        row.Values.TryGetValue("TotalCost", out var totalCost);
        row.Values.TryGetValue("TotalSale", out var totalSale);
        row.Values.TryGetValue("Profit", out var profit);
        row.Values.TryGetValue("Margin", out var margin);

        row.Values.TryGetValue("SpaceComment", out var spaceComment);
        row.Values.TryGetValue("Remarks", out var remarks);

        return PricingExtractionRecord.Create(
            extractionExecutionId,
            sourceDocumentId,
            row.SourceSheetName,
            row.SourceRowNumber,
            PortNameNormalizer.Normalize(originPort),
            PortNameNormalizer.Normalize(portOfExit),
            PortNameNormalizer.Normalize(destinationPort),
            ContainerTypeNormalizer.Normalize(containerType),
            CarrierNameNormalizer.Normalize(carrier),
            NormalizeText(agent),
            NormalizeText(commodity),
            NormalizeCurrency(currency),
            DateNormalizer.Normalize(validFrom),
            DateNormalizer.Normalize(validTo),
            MoneyNormalizer.Normalize(oceanFreight),
            MoneyNormalizer.Normalize(originCharges),
            MoneyNormalizer.Normalize(destinationCharges),
            MoneyNormalizer.Normalize(surcharges),
            MoneyNormalizer.Normalize(totalCost),
            MoneyNormalizer.Normalize(totalSale),
            MoneyNormalizer.Normalize(profit),
            MoneyNormalizer.Normalize(margin),
            NormalizeText(spaceComment),
            NormalizeText(remarks),
            row.RawJson,
            createdBy
        );
    }

    private static string? NormalizeText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeCurrency(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    }
}
