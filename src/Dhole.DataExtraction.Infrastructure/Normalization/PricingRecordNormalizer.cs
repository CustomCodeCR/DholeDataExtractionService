using System.Text.RegularExpressions;
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
        var originPort = Get(row, "OriginPort");
        var portOfExit = Get(row, "PortOfExit");
        var destinationPort = Get(row, "DestinationPort");
        var containerType = Get(row, "ContainerType");
        var carrier = Get(row, "Carrier");
        var agent = Get(row, "Agent");
        var commodity = Get(row, "Commodity");
        var currency = Get(row, "Currency");
        var freeDays = Days(row, "FreeDays");
        var transitDays = Days(row, "TransitDays");

        var validFrom = Get(row, "ValidFrom");
        var validTo = Get(row, "ValidTo");

        var oceanFreight = Money(row, "OceanFreight");

        var originCharges = FirstMoney(row, "OriginCharges")
            ?? SumMoney(row, "AgentProfitCost", "AgentReleaseCost");

        var destinationCharges = FirstMoney(row, "DestinationCharges")
            ?? SumMoney(
                row,
                "DestinationThcCost",
                "DocumentationCost",
                "ContainerProtectCost",
                "WharfageCost",
                "MerchantCost"
            );

        var surcharges = FirstMoney(row, "Surcharges")
            ?? SumMoney(
                row,
                "InternalFreightCost",
                "CarouselCost",
                "PanamaHandlingCost",
                "InternationalLandFreightCost",
                "BunkerCost"
            );

        var totalCost = Money(row, "TotalCost")
            ?? SumMoney(oceanFreight, originCharges, destinationCharges, surcharges);

        var totalSale = FirstMoney(
            row,
            "TotalSale",
            "AllInSale",
            "InternationalFreightSale"
        );

        var profit = Money(row, "Profit") ?? ComputeProfit(totalSale, totalCost);
        var margin = FirstMoney(row, "Margin") ?? ComputeMargin(profit, totalSale);
        var normalizedCurrency = NormalizeCurrency(currency) ?? InferCurrency(row);

        var spaceComment = Get(row, "SpaceComment");
        var remarks = FirstText(row, "Remarks", "RouteMode");

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
            normalizedCurrency,
            freeDays,
            transitDays,
            DateNormalizer.Normalize(validFrom),
            DateNormalizer.Normalize(validTo),
            oceanFreight,
            originCharges,
            destinationCharges,
            surcharges,
            totalCost,
            totalSale,
            profit,
            margin,
            NormalizeText(spaceComment),
            NormalizeText(remarks),
            row.RawJson,
            createdBy
        );
    }

    private static string? Get(MappedPricingRow row, string key)
    {
        return row.Values.TryGetValue(key, out var value) ? value : null;
    }

    private static string? FirstText(MappedPricingRow row, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = NormalizeText(Get(row, key));

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static decimal? Money(MappedPricingRow row, string key)
    {
        return MoneyNormalizer.Normalize(Get(row, key));
    }

    private static int? Days(MappedPricingRow row, string key)
    {
        var value = Get(row, key);

        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = Regex.Match(value, @"-?\d+");
        if (!match.Success || !int.TryParse(match.Value, out var days) || days < 0)
        {
            return null;
        }

        return days;
    }

    private static decimal? FirstMoney(MappedPricingRow row, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Money(row, key);

            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static decimal? SumMoney(MappedPricingRow row, params string[] keys)
    {
        decimal total = 0;
        var hasValue = false;

        foreach (var key in keys)
        {
            var value = Money(row, key);

            if (value is null)
            {
                continue;
            }

            total += value.Value;
            hasValue = true;
        }

        return hasValue ? total : null;
    }

    private static decimal? SumMoney(params decimal?[] values)
    {
        decimal total = 0;
        var hasValue = false;

        foreach (var value in values)
        {
            if (value is null)
            {
                continue;
            }

            total += value.Value;
            hasValue = true;
        }

        return hasValue ? total : null;
    }

    private static decimal? ComputeProfit(decimal? totalSale, decimal? totalCost)
    {
        if (totalSale is null || totalCost is null)
        {
            return null;
        }

        return totalSale.Value - totalCost.Value;
    }

    private static decimal? ComputeMargin(decimal? profit, decimal? totalSale)
    {
        if (profit is null || totalSale is null || totalSale.Value == 0)
        {
            return null;
        }

        return Math.Round((profit.Value / totalSale.Value) * 100m, 4);
    }

    private static string? InferCurrency(MappedPricingRow row)
    {
        var moneyKeys = new[]
        {
            "OceanFreight",
            "TotalCost",
            "TotalSale",
            "Profit",
            "AgentProfitCost",
            "AgentReleaseCost",
            "DestinationThcCost",
            "DocumentationCost",
            "ContainerProtectCost",
            "WharfageCost",
            "MerchantCost",
            "InternalFreightCost",
            "CarouselCost",
            "PanamaHandlingCost",
            "InternationalLandFreightCost",
            "BunkerCost",
            "InternationalFreightSale",
            "AllInSale",
            "DestinationChargesSale",
            "CarouselSale",
            "InternalFreightSale",
            "HandlingSale",
        };

        return moneyKeys.Any(key => Money(row, key) is not null) ? "USD" : null;
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
