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

                    targetField = PricingRouteFieldSemantics.ResolveTargetField(
                        normalizedHeader,
                        targetField
                    );

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
                    foreach (
                        var matrixRow in matrixRows
                            .SelectMany(ExpandContainerVariants)
                            .SelectMany(ExpandRouteVariants)
                    )
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

                foreach (
                    var expandedValues in ExpandContainerVariants(values)
                        .SelectMany(ExpandRouteVariants)
                )
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
            || mappedValues.ContainsKey("PortOfExit")
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
            var containerTypes = PricingContainerVariants.Expand(item.Key);

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
        return PricingContainerVariants.Expand(header).Count > 0;
    }

    private static IReadOnlyCollection<IReadOnlyDictionary<string, string?>>
        ExpandContainerVariants(IReadOnlyDictionary<string, string?> values)
    {
        if (
            !values.TryGetValue("ContainerType", out var containerValue)
            || string.IsNullOrWhiteSpace(containerValue)
        )
        {
            return [values];
        }

        var variants = PricingContainerVariants.Expand(containerValue);
        if (variants.Count == 0)
        {
            return [values];
        }

        return variants
            .Select(containerType =>
            {
                var clone = new Dictionary<string, string?>(values)
                {
                    ["ContainerType"] = containerType,
                };
                return (IReadOnlyDictionary<string, string?>)clone;
            })
            .ToArray();
    }

    private static IReadOnlyCollection<IReadOnlyDictionary<string, string?>> ExpandRouteVariants(
        IReadOnlyDictionary<string, string?> values
    )
    {
        var origins = SplitRouteVariants(values.TryGetValue("OriginPort", out var originValue) ? originValue : null);
        var portsOfExit = SplitRouteVariants(
            values.TryGetValue("PortOfExit", out var portOfExitValue)
                ? portOfExitValue
                : null
        );
        var destinations = SplitRouteVariants(values.TryGetValue("DestinationPort", out var destinationValue) ? destinationValue : null);

        if (origins.Count == 0)
        {
            origins = [null];
        }

        if (portsOfExit.Count == 0)
        {
            portsOfExit = [null];
        }

        if (destinations.Count == 0)
        {
            destinations = [null];
        }

        if (
            origins.Count == 1
            && portsOfExit.Count == 1
            && destinations.Count == 1
            && origins[0] is null
            && portsOfExit[0] is null
            && destinations[0] is null
        )
        {
            return [values];
        }

        var result = new List<IReadOnlyDictionary<string, string?>>();

        foreach (var originVariant in origins)
        {
            foreach (var portOfExitVariant in portsOfExit)
            {
                foreach (var destinationVariant in destinations)
                {
                    var clone = new Dictionary<string, string?>(values);

                    if (!string.IsNullOrWhiteSpace(originVariant))
                    {
                        clone["OriginPort"] = originVariant;
                    }

                    if (!string.IsNullOrWhiteSpace(portOfExitVariant))
                    {
                        clone["PortOfExit"] = portOfExitVariant;
                    }

                    if (!string.IsNullOrWhiteSpace(destinationVariant))
                    {
                        clone["DestinationPort"] = destinationVariant;
                    }

                    result.Add(clone);
                }
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
        if (PricingBasePorts.IsChinaOrAsiaBasePorts(normalized))
        {
            return PricingBasePorts.China.Cast<string?>().ToArray();
        }

        var parts = value
            .Split(['/', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(x => Regex.Split(x, @"\s+(?:and|y)\s+", RegexOptions.IgnoreCase))
            .Select(x => x.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select<string, string?>(x =>
            {
                var partNormalized = ColumnHeaderNormalizer.Normalize(x);
                return PricingBasePorts.IsChinaOrAsiaBasePorts(partNormalized)
                    ? "__DH_CHINA_BASE_PORTS__"
                    : x;
            })
            .ToArray();

        if (
            parts.Any(x =>
                string.Equals(
                    x,
                    "__DH_CHINA_BASE_PORTS__",
                    StringComparison.OrdinalIgnoreCase
                )
            )
        )
        {
            return PricingBasePorts.China.Cast<string?>().ToArray();
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
            mappings[rule.NormalizedSourceColumnName] =
                PricingRouteFieldSemantics.ResolveTargetField(
                    rule.NormalizedSourceColumnName,
                    rule.TargetField
                );
        }

        return mappings;
    }

    private static bool IsRequiredTarget(string? targetField)
    {
        return targetField
            is "OriginPort"
                or "PortOfExit"
                or "ContainerType"
                or "Carrier"
                or "Currency";
    }
}
