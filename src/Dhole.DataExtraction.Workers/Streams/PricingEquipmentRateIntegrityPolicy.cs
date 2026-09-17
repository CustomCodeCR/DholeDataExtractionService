using System.Globalization;
using System.Text.RegularExpressions;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Contracts.Extraction;

namespace Dhole.DataExtraction.Workers.Streams;

/// <summary>
/// Reconciles equipment/freight associations against explicit evidence in the email or
/// attachment text. It is deliberately conservative: a value is corrected only when the
/// source contains one unambiguous freight amount for that equipment. This prevents a
/// model from copying a 20DV amount into 40DV/40HC while avoiding guesses when a document
/// contains several routes or several rates for the same equipment.
/// </summary>
public static partial class PricingEquipmentRateIntegrityPolicy
{
    private static readonly Regex ReplyBoundaryRegex = new(
        @"(?im)^\s*(?:-{2,}\s*(?:ORIGINAL MESSAGE|MENSAJE ORIGINAL)\s*-{2,}|(?:FROM|DE|SENT|ENVIADO)\s*:.*|ON\s+.{1,160}\s+WROTE\s*:|EL\s+.{1,160}\s+ESCRIBI[ÓO]\s*:).*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    [GeneratedRegex(
        @"(?ix)(?<equipment>20\s*(?:FT|')?\s*(?:DV|GP|DC)?|40\s*(?:FT|')?\s*(?:HC|HQ|DV|GP|DC))\s*(?:[:=\-|]\s*|(?:USD|US\$|\$|EUR|CRC|GBP|CAD)\s+)(?<currency>USD|US\$|\$|EUR|CRC|GBP|CAD)?\s*(?<amount>\d{2,6}(?:[.,]\d{1,2})?)"
    )]
    private static partial Regex InlineEquipmentAmountRegex();

    [GeneratedRegex(
        @"(?ix)(?<equipment>20\s*(?:FT|')?\s*(?:DV|GP|DC)?|40\s*(?:FT|')?\s*(?:HC|HQ|DV|GP|DC))"
    )]
    private static partial Regex EquipmentRegex();

    [GeneratedRegex(
        @"(?ix)(?<!\d)(?:(?:USD|US\$|\$|EUR|CRC|GBP|CAD)\s*)?(?<amount>\d{2,6}(?:[.,]\d{1,2})?)(?!\d)"
    )]
    private static partial Regex AmountRegex();

    public static AiPricingEmailRow[] Reconcile(
        IReadOnlyCollection<AiPricingEmailRow> rows,
        string? subject,
        string? bodyText,
        string? sourceContent,
        out int corrections
    )
    {
        var evidence = BuildEvidence(subject, bodyText, sourceContent);
        var freightByEquipment = ExtractUnambiguousFreightByEquipment(evidence);
        var correctionCount = 0;

        if (freightByEquipment.Count == 0)
        {
            corrections = 0;
            return rows.ToArray();
        }

        var reconciled = rows.Select(row =>
        {
            var key = NormalizeEquipment(row.ContainerType);
            if (
                key is null
                || !freightByEquipment.TryGetValue(key, out var freight)
                || row.OceanFreight == freight
            )
            {
                return row;
            }

            correctionCount++;
            return row with { OceanFreight = freight };
        }).ToArray();

        corrections = correctionCount;
        return reconciled;
    }

    public static ExtractedPricingRowDto[] Reconcile(
        IReadOnlyCollection<ExtractedPricingRowDto> rows,
        string? subject,
        string? bodyText,
        string? sourceContent,
        out int corrections
    )
    {
        var evidence = BuildEvidence(subject, bodyText, sourceContent);
        var freightByEquipment = ExtractUnambiguousFreightByEquipment(evidence);
        var correctionCount = 0;

        if (freightByEquipment.Count == 0)
        {
            corrections = 0;
            return rows.ToArray();
        }

        var reconciled = rows.Select(row =>
        {
            var key = NormalizeEquipment(row.ContainerType);
            if (
                key is null
                || !freightByEquipment.TryGetValue(key, out var freight)
                || row.OceanFreight == freight
            )
            {
                return row;
            }

            correctionCount++;
            return row with { OceanFreight = freight };
        }).ToArray();

        corrections = correctionCount;
        return reconciled;
    }

    private static IReadOnlyDictionary<string, decimal> ExtractUnambiguousFreightByEquipment(
        string evidence
    )
    {
        if (string.IsNullOrWhiteSpace(evidence))
        {
            return new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        }

        var candidates = new Dictionary<string, HashSet<decimal>>(
            StringComparer.OrdinalIgnoreCase
        );
        var lines = evidence
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            ExtractInlinePairs(line, candidates);
        }

        for (var index = 0; index < lines.Length - 1; index++)
        {
            ExtractHeaderMatrix(lines[index], lines[index + 1], candidates);
        }

        return candidates
            .Where(item => item.Value.Count == 1)
            .ToDictionary(
                item => item.Key,
                item => item.Value.Single(),
                StringComparer.OrdinalIgnoreCase
            );
    }

    private static void ExtractInlinePairs(
        string line,
        IDictionary<string, HashSet<decimal>> candidates
    )
    {
        foreach (Match match in InlineEquipmentAmountRegex().Matches(line))
        {
            var equipment = NormalizeEquipment(match.Groups["equipment"].Value);
            if (
                equipment is null
                || !TryParseMoney(match.Groups["amount"].Value, out var amount)
                || !IsPlausibleFreight(amount)
            )
            {
                continue;
            }

            AddCandidate(candidates, equipment, amount);
        }
    }

    private static void ExtractHeaderMatrix(
        string headerLine,
        string amountLine,
        IDictionary<string, HashSet<decimal>> candidates
    )
    {
        var equipmentMatches = EquipmentRegex().Matches(headerLine).Cast<Match>().ToArray();
        if (equipmentMatches.Length < 2)
        {
            return;
        }

        var equipment = equipmentMatches
            .Select(match => NormalizeEquipment(match.Groups["equipment"].Value))
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();
        if (equipment.Length < 2)
        {
            return;
        }

        var amounts = AmountRegex().Matches(amountLine)
            .Cast<Match>()
            .Select(match => match.Groups["amount"].Value)
            .Select(value => TryParseMoney(value, out var amount) ? amount : (decimal?)null)
            .Where(value => value.HasValue && IsPlausibleFreight(value.Value))
            .Select(value => value!.Value)
            .ToArray();

        if (amounts.Length != equipment.Length)
        {
            return;
        }

        for (var index = 0; index < equipment.Length; index++)
        {
            AddCandidate(candidates, equipment[index], amounts[index]);
        }
    }

    private static void AddCandidate(
        IDictionary<string, HashSet<decimal>> candidates,
        string equipment,
        decimal amount
    )
    {
        if (!candidates.TryGetValue(equipment, out var values))
        {
            values = [];
            candidates[equipment] = values;
        }

        values.Add(amount);
    }

    private static string? NormalizeEquipment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var compact = Regex.Replace(
                value.ToUpperInvariant(),
                @"[\s_\-./]",
                string.Empty,
                RegexOptions.CultureInvariant
            )
            .Replace("FEET", "", StringComparison.Ordinal)
            .Replace("FT", "", StringComparison.Ordinal)
            .Replace("'", "", StringComparison.Ordinal);

        if (!compact.StartsWith("20", StringComparison.Ordinal)
            && !compact.StartsWith("40", StringComparison.Ordinal))
        {
            return null;
        }

        if (compact.StartsWith("20", StringComparison.Ordinal))
        {
            return "20DV";
        }

        return compact.Contains("HC", StringComparison.Ordinal)
            || compact.Contains("HQ", StringComparison.Ordinal)
            ? "40HC"
            : "40DV";
    }

    private static bool TryParseMoney(string rawValue, out decimal amount)
    {
        var normalized = rawValue.Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
        if (normalized.Contains(',') && normalized.Contains('.'))
        {
            normalized = normalized.LastIndexOf(',') > normalized.LastIndexOf('.')
                ? normalized.Replace(".", string.Empty, StringComparison.Ordinal).Replace(',', '.')
                : normalized.Replace(",", string.Empty, StringComparison.Ordinal);
        }
        else if (normalized.Count(character => character == ',') == 1)
        {
            var commaIndex = normalized.IndexOf(',');
            var decimals = normalized.Length - commaIndex - 1;
            normalized = decimals is 1 or 2
                ? normalized.Replace(',', '.')
                : normalized.Replace(",", string.Empty, StringComparison.Ordinal);
        }

        return decimal.TryParse(
            normalized,
            NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out amount
        );
    }

    private static bool IsPlausibleFreight(decimal amount) => amount >= 50m && amount <= 100000m;

    private static string BuildEvidence(string? subject, string? bodyText, string? sourceContent)
    {
        return string.Join(
            '\n',
            new[] { subject, SelectCurrentMessage(bodyText), SelectCurrentMessage(sourceContent) }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
        );
    }

    private static string? SelectCurrentMessage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal);
        var boundary = ReplyBoundaryRegex.Match(normalized);
        return boundary.Success && boundary.Index > 20
            ? normalized[..boundary.Index].Trim()
            : normalized.Trim();
    }
}
