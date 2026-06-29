using System.Text.Json;
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

                    values[targetField] = item.Value;
                }

                result.Add(
                    new MappedPricingRow(
                        table.SheetName,
                        row.RowNumber,
                        values,
                        JsonSerializer.Serialize(row.Values)
                    )
                );
            }
        }

        return result;
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
