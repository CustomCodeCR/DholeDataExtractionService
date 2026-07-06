using System.Text.Json;
using System.Text.RegularExpressions;
using Dhole.DataExtraction.Infrastructure.Normalization;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Application.Abstractions.Repositories;

namespace Dhole.DataExtraction.Infrastructure.Mapping;

public sealed class ColumnMappingService(IColumnMappingProfileRepository profiles)
    : IColumnMappingService
{
    public async Task<IReadOnlyCollection<MappedPricingRow>> MapAsync(
        ExtractedDocument document,
        string? profileCode = null,
        CancellationToken cancellationToken = default
    )
    {
        var mappings = await BuildMappingsAsync(profileCode, cancellationToken);
        var result = new List<MappedPricingRow>();

        foreach (var table in document.Tables)
        {
            foreach (var row in table.Rows)
            {
                var values = new Dictionary<string, string?>();

                foreach (var item in row.Values)
                {
                    var normalizedHeader = ColumnHeaderNormalizer.Normalize(item.Key);

                    if (!mappings.TryGetValue(normalizedHeader, out var targetField))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(item.Value)
                        && values.TryGetValue(targetField, out var existingValue)
                        && !string.IsNullOrWhiteSpace(existingValue))
                    {
                        continue;
                    }

                    values[targetField] = item.Value;
                }

                var matrixRows = BuildMatrixRows(values, row.Values);

                if (matrixRows.Count > 0)
                {
                    foreach (var matrixRow in matrixRows.SelectMany(ExpandRouteVariants))
                    {
                        result.Add(
                            new MappedPricingRow(
                                table.SheetName,
                                row.RowNumber,
                                matrixRow,
                                JsonSerializer.Serialize(row.Values)
                            )
                        );
                    }

                    continue;
                }

                if (values.Count == 0 || values.Values.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                foreach (var expandedValues in ExpandRouteVariants(values))
                {
                    result.Add(
                        new MappedPricingRow(
                            table.SheetName,
                            row.RowNumber,
                            expandedValues,
                            JsonSerializer.Serialize(row.Values)
                        )
                    );
                }
            }
        }

        return result;
    }

    private static IReadOnlyCollection<IReadOnlyDictionary<string, string?>> BuildMatrixRows(
        IReadOnlyDictionary<string, string?> mappedValues,
        IReadOnlyDictionary<string, string?> sourceValues
    )
    {
        var matrixAmountCells = sourceValues
            .Where(item => IsContainerAmountHeader(item.Key)
                && !string.IsNullOrWhiteSpace(item.Value)
                && MoneyNormalizer.Normalize(item.Value) is not null)
            .ToArray();

        if (matrixAmountCells.Length == 0)
        {
            return [];
        }

        var hasRouteData = mappedValues.ContainsKey("OriginPort")
            || mappedValues.ContainsKey("DestinationPort")
            || mappedValues.ContainsKey("Carrier")
            || mappedValues.ContainsKey("Currency");

        if (!hasRouteData)
        {
            return [];
        }

        var rows = new List<IReadOnlyDictionary<string, string?>>();

        foreach (var item in matrixAmountCells)
        {
            var containerTypes = NormalizeContainerHeaders(item.Key);

            foreach (var containerType in containerTypes)
            {
                var values = new Dictionary<string, string?>(mappedValues)
                {
                    ["ContainerType"] = containerType
                };

                if (LooksLikeSaleHeader(item.Key))
                {
                    values["TotalSale"] = item.Value;
                }
                else
                {
                    values["OceanFreight"] = item.Value;
                }

                rows.Add(values);
            }
        }

        return rows;
    }

    private static bool IsContainerAmountHeader(string? header)
    {
        return NormalizeContainerHeaders(header).Count > 0;
    }

    private static IReadOnlyCollection<string> NormalizeContainerHeaders(string? header)
    {
        var normalized = ColumnHeaderNormalizer.Normalize(header);

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        var result = new List<string>();

        void Add(string containerType)
        {
            if (!result.Contains(containerType, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(containerType);
            }
        }

        var has20 = Regex.IsMatch(normalized, @"(^|[^0-9])20([^0-9]|$)")
            || normalized.StartsWith("20")
            || normalized.Contains("20gp")
            || normalized.Contains("20dc")
            || normalized.Contains("20dv")
            || normalized.Contains("20std")
            || normalized.Contains("20ft")
            || normalized.Contains("20dry");

        var has40Hc = normalized.Contains("40hc")
            || normalized.Contains("40hq")
            || normalized.Contains("40highcube");

        var hasExplicit40Dry = normalized.Contains("40gp")
            || normalized.Contains("40dc")
            || normalized.Contains("40dv")
            || normalized.Contains("40std")
            || normalized.Contains("40ft")
            || normalized.Contains("40dry");

        var hasBare40 = normalized == "40"
            || Regex.IsMatch(normalized, @"^40(usd|eur|crc|rate|rates|freight|flete|tarifa|amount|costo|venta|sale|allin|oceanfreight)?$");

        var hasCompound40And40Hc = has40Hc
            && (header?.Contains('/') == true
                || header?.Contains('\\') == true
                || normalized.StartsWith("4040")
                || normalized.Contains("40gp40")
                || normalized.Contains("40dv40")
                || normalized.Contains("40dc40"));

        var hasPlain40 = hasExplicit40Dry || hasBare40 || hasCompound40And40Hc;

        var has45Hc = normalized.Contains("45hc") || normalized.Contains("45hq");

        if (has20)
        {
            Add("20DV");
        }

        if (hasPlain40 && has40Hc)
        {
            Add("40DV");
            Add("40HC");
        }
        else if (has40Hc)
        {
            Add("40HC");
        }
        else if (hasPlain40)
        {
            Add("40DV");
        }

        if (has45Hc)
        {
            Add("45HC");
        }

        return result;
    }

    private static IReadOnlyCollection<IReadOnlyDictionary<string, string?>> ExpandRouteVariants(
        IReadOnlyDictionary<string, string?> values
    )
    {
        var origins = SplitRouteVariants(values.TryGetValue("OriginPort", out var originValue) ? originValue : null);
        var destinations = SplitRouteVariants(values.TryGetValue("DestinationPort", out var destinationValue) ? destinationValue : null);

        if (origins.Count == 0)
        {
            origins = [null];
        }

        if (destinations.Count == 0)
        {
            destinations = [null];
        }

        if (origins.Count == 1 && destinations.Count == 1 && origins[0] is null && destinations[0] is null)
        {
            return [values];
        }

        var result = new List<IReadOnlyDictionary<string, string?>>();

        foreach (var originVariant in origins)
        {
            foreach (var destinationVariant in destinations)
            {
                var clone = new Dictionary<string, string?>(values);

                if (!string.IsNullOrWhiteSpace(originVariant))
                {
                    clone["OriginPort"] = originVariant;
                }

                if (!string.IsNullOrWhiteSpace(destinationVariant))
                {
                    clone["DestinationPort"] = destinationVariant;
                }

                result.Add(clone);
            }
        }

        return result.Count == 0 ? [values] : result;
    }

    private static IReadOnlyList<string?> SplitRouteVariants(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [null];
        }

        var normalized = ColumnHeaderNormalizer.Normalize(value);
        if (normalized is "chinabaseports" or "chinabaseport" or "baseportschina" or "baseportchina")
        {
            return ["NINGBO", "SHANGHAI", "QINGDAO"];
        }

        var parts = value
            .Split(['/', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(x => Regex.Split(x, @"\s+(?:and|y)\s+", RegexOptions.IgnoreCase))
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select<string, string?>(x =>
            {
                var partNormalized = ColumnHeaderNormalizer.Normalize(x);
                return partNormalized is "chinabaseports" or "chinabaseport" or "baseportschina" or "baseportchina"
                    ? "NINGBO/SHANGHAI/QINGDAO"
                    : x;
            })
            .ToArray();

        if (parts.Any(x => string.Equals(x, "NINGBO/SHANGHAI/QINGDAO", StringComparison.OrdinalIgnoreCase)))
        {
            return ["NINGBO", "SHANGHAI", "QINGDAO"];
        }

        return parts.Length <= 1 ? [value.Trim()] : parts;
    }

    private static bool LooksLikeSaleHeader(string? header)
    {
        var normalized = ColumnHeaderNormalizer.Normalize(header);

        return normalized.Contains("venta")
            || normalized.Contains("sale")
            || normalized.Contains("allin");
    }

    public async Task<ColumnMappingPreviewResult> PreviewAsync(
        ExtractedDocument document,
        string? profileCode = null,
        CancellationToken cancellationToken = default
    )
    {
        var mappings = await BuildMappingsAsync(profileCode, cancellationToken);

        var headers = document
            .Tables.SelectMany(x => x.Headers)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var items = headers
            .Select(header =>
            {
                var normalized = ColumnHeaderNormalizer.Normalize(header);
                var mapped = mappings.TryGetValue(normalized, out var targetField);

                return new ColumnMappingPreviewItem(
                    header,
                    normalized,
                    targetField,
                    mapped,
                    IsRequiredTarget(targetField)
                );
            })
            .ToArray();

        return new ColumnMappingPreviewResult(profileCode, items);
    }

    private async Task<IReadOnlyDictionary<string, string>> BuildMappingsAsync(
        string? profileCode,
        CancellationToken cancellationToken
    )
    {
        var mappings = new Dictionary<string, string>(DefaultFclColumnMappings.Mappings);

        if (string.IsNullOrWhiteSpace(profileCode))
        {
            return mappings;
        }

        var profile = await profiles.GetActiveByCodeAsync(profileCode, cancellationToken);

        if (profile is null)
        {
            return mappings;
        }

        foreach (var rule in profile.Rules.Where(x => x.IsActive && !x.IsDeleted))
        {
            mappings[rule.NormalizedSourceColumnName] = rule.TargetField;
        }

        return mappings;
    }

    private static bool IsRequiredTarget(string? targetField)
    {
        return targetField
            is "OriginPort"
                or "DestinationPort"
                or "ContainerType"
                or "Carrier"
                or "Currency";
    }
}
