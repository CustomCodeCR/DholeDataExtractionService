using System.Globalization;
using System.Text.RegularExpressions;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Contracts.Extraction;

namespace Dhole.DataExtraction.Workers.Streams;

/// <summary>
/// Enforces the commercial semantics of the approved pricing extraction template after
/// AI extraction and after deterministic recovery. The template is the source of truth
/// for Tarifa SPOT, ETD and Observaciones even when the model omits those details.
/// </summary>
public static class PricingExtractionTemplatePolicy
{
    private static readonly Regex SpotRateRegex = new(
        @"(?im)(?:\bTIPO\s*(?:DE\s*)?TARIFA\b[\s:=\-|]{0,40}\bSPOT\b|\b(?:TARIFA|RATE|TARIFF)\s*(?:TYPE\s*)?[:=\-]?\s*SPOT\b|\bSPOT\s+(?:RATE|TARIFF|TARIFA)\b)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private static readonly Regex SubjectSpotRegex = new(
        @"\bSPOT\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    private static readonly Regex EtdDateRegex = new(
        @"(?im)\bETD(?:\s+DATE)?\b\s*[:=\-]?\s*(?<date>(?:\d{4}[/-]\d{1,2}[/-]\d{1,2})|(?:\d{1,2}[/-]\d{1,2}(?:[/-]\d{2,4})?)|(?:\d{1,2}(?:st|nd|rd|th)?\s+[A-Za-zÁÉÍÓÚÜÑáéíóúüñ]{3,12}(?:\s+\d{2,4})?)|(?:[A-Za-zÁÉÍÓÚÜÑáéíóúüñ]{3,12}\s+\d{1,2}(?:st|nd|rd|th)?(?:,?\s+\d{2,4})?))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private static readonly Regex CommodityRegex = new(
        @"(?im)^\s*(?:COMMODITY|MERCANC[IÍ]A)\s*[:=\-]\s*(?<value>[^\r\n|;]{1,120})",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private static readonly Regex ReplyBoundaryRegex = new(
        @"(?im)^\s*(?:-{2,}\s*(?:ORIGINAL MESSAGE|MENSAJE ORIGINAL)\s*-{2,}|(?:FROM|DE|SENT|ENVIADO)\s*:.*|ON\s+.{1,160}\s+WROTE\s*:|EL\s+.{1,160}\s+ESCRIBI[ÓO]\s*:).*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    private static readonly string[] NumericDateFormats =
    [
        "yyyy-M-d",
        "yyyy/M/d",
        "d/M/yyyy",
        "d-M-yyyy",
        "d/M/yy",
        "d-M-yy",
        "M/d/yyyy",
        "M-d-yyyy",
        "M/d/yy",
        "M-d-yy",
    ];

    public static AiPricingEmailAnalysisResult Apply(
        AiPricingEmailAnalysisResult result,
        string? subject,
        string? bodyText,
        string? sourceContent,
        DateTime utcNow
    )
    {
        var context = BuildContext(subject, bodyText, sourceContent, utcNow);
        var rows = result.Rows.Select(row => Apply(row, context)).ToArray();
        return result with { Rows = rows };
    }

    public static ExtractPricingDataResponse Apply(
        ExtractPricingDataResponse response,
        string? subject,
        string? bodyText,
        string? sourceContent,
        DateTime utcNow
    )
    {
        var context = BuildContext(subject, bodyText, sourceContent, utcNow);
        var rows = response.Rows.Select(row => Apply(row, context)).ToArray();
        var issues = response.Issues;
        var summary = response.Summary;

        if (context.IsSpot)
        {
            // For SPOT rates the template rule supplies the validity explicitly as
            // today -> today. Any old missing-validity diagnostics are therefore stale.
            issues = response.Issues
                .Where(issue => !IsSpotValidityIssue(issue.Code))
                .ToArray();

            var hasBlockingIssue = issues.Any(issue => issue.IsBlocking);
            if (!hasBlockingIssue && rows.Length > 0)
            {
                summary = summary with
                {
                    ValidRows = Math.Max(summary.ValidRows, rows.Length),
                    InvalidRows = 0,
                    HasIssues = issues.Count > 0,
                };
            }
            else
            {
                summary = summary with { HasIssues = issues.Count > 0 };
            }
        }

        return response with
        {
            Rows = rows,
            Issues = issues,
            Summary = summary,
        };
    }

    private static AiPricingEmailRow Apply(
        AiPricingEmailRow row,
        TemplateContext context
    )
    {
        var commodity = FirstText(row.Commodity, context.CommodityHint);
        return row with
        {
            Commodity = commodity,
            ValidFrom = context.IsSpot ? context.Today : row.ValidFrom,
            ValidTo = context.IsSpot ? context.Today : row.ValidTo,
            SpaceComment = BuildComments(
                row.SpaceComment,
                context.IsSpot,
                context.Etd,
                commodity
            ),
        };
    }

    private static ExtractedPricingRowDto Apply(
        ExtractedPricingRowDto row,
        TemplateContext context
    )
    {
        var commodity = FirstText(row.Commodity, context.CommodityHint);
        return row with
        {
            Commodity = commodity,
            ValidFrom = context.IsSpot ? context.Today : row.ValidFrom,
            ValidTo = context.IsSpot ? context.Today : row.ValidTo,
            SpaceComment = BuildComments(
                row.SpaceComment,
                context.IsSpot,
                context.Etd,
                commodity
            ),
        };
    }

    private static TemplateContext BuildContext(
        string? subject,
        string? bodyText,
        string? sourceContent,
        DateTime utcNow
    )
    {
        var today = CostaRicaToday(utcNow);
        var currentBody = SelectCurrentMessage(bodyText);
        var currentSource = SelectCurrentMessage(sourceContent);
        var evidence = string.Join(
            '\n',
            new[] { subject, currentBody, currentSource }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim())
        );

        var isSpot = SubjectSpotRegex.IsMatch(subject ?? string.Empty)
            || SpotRateRegex.IsMatch(evidence);
        var etd = ExtractSingleEtd(evidence, today.Year);
        var commodityHint = ExtractCommodityHint(evidence);

        return new TemplateContext(isSpot, today, etd, commodityHint);
    }

    private static DateTime CostaRicaToday(DateTime utcNow)
    {
        var normalizedUtc = utcNow.Kind == DateTimeKind.Utc
            ? utcNow
            : DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);

        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Costa_Rica");
            return TimeZoneInfo.ConvertTimeFromUtc(normalizedUtc, zone).Date;
        }
        catch (TimeZoneNotFoundException)
        {
            return normalizedUtc.AddHours(-6).Date;
        }
        catch (InvalidTimeZoneException)
        {
            return normalizedUtc.AddHours(-6).Date;
        }
    }

    private static DateTime? ExtractSingleEtd(string evidence, int fallbackYear)
    {
        if (string.IsNullOrWhiteSpace(evidence))
        {
            return null;
        }

        var dates = EtdDateRegex.Matches(evidence)
            .Select(match => TryParseDate(match.Groups["date"].Value, fallbackYear))
            .Where(date => date.HasValue)
            .Select(date => date!.Value.Date)
            .Distinct()
            .Take(2)
            .ToArray();

        // A single ETD can safely be propagated to all rows. If a source contains
        // several ETDs, do not attach the wrong departure date to every tariff row.
        return dates.Length == 1 ? dates[0] : null;
    }

    private static DateTime? TryParseDate(string rawValue, int fallbackYear)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return null;
        }

        var value = rawValue.Trim();
        value = Regex.Replace(
            value,
            @"(?<=\d)(?:st|nd|rd|th)\b",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
        );
        value = value.Replace("setiembre", "septiembre", StringComparison.OrdinalIgnoreCase);

        foreach (var format in NumericDateFormats)
        {
            if (DateTime.TryParseExact(
                value,
                format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out var exact
            ))
            {
                return exact.Date;
            }
        }

        var hasYear = Regex.IsMatch(value, @"\b\d{4}\b", RegexOptions.CultureInvariant)
            || Regex.IsMatch(value, @"\d{1,2}[/-]\d{1,2}[/-]\d{2,4}", RegexOptions.CultureInvariant);
        if (!hasYear)
        {
            value = $"{value} {fallbackYear}";
        }

        foreach (var cultureName in new[] { "es-CR", "en-US", "en-GB" })
        {
            if (DateTime.TryParse(
                value,
                CultureInfo.GetCultureInfo(cultureName),
                DateTimeStyles.AllowWhiteSpaces,
                out var parsed
            ))
            {
                return parsed.Date;
            }
        }

        return null;
    }

    private static string? ExtractCommodityHint(string evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence))
        {
            return null;
        }

        var values = CommodityRegex.Matches(evidence)
            .Select(match => match.Groups["value"].Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .ToArray();

        return values.Length == 1 ? values[0] : null;
    }

    private static string? BuildComments(
        string? existing,
        bool isSpot,
        DateTime? etd,
        string? commodity
    )
    {
        var comments = new List<string>();
        if (!string.IsNullOrWhiteSpace(existing))
        {
            comments.Add(existing.Trim());
        }

        AppendLabeledComment(comments, "Tipo Tarifa", isSpot ? "SPOT" : null);
        AppendLabeledComment(
            comments,
            "ETD",
            etd?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        );
        AppendLabeledComment(comments, "Commodity", commodity);

        return comments.Count == 0 ? null : string.Join(Environment.NewLine, comments);
    }

    private static void AppendLabeledComment(
        ICollection<string> comments,
        string label,
        string? value
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        var existing = string.Join(Environment.NewLine, comments);
        if (Regex.IsMatch(
            existing,
            $@"(?im)^\s*{Regex.Escape(label)}\s*:",
            RegexOptions.CultureInvariant
        ))
        {
            return;
        }

        comments.Add($"{label}: {value.Trim()}");
    }

    private static string? SelectCurrentMessage(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal);
        var boundary = ReplyBoundaryRegex.Match(normalized);
        if (boundary.Success && boundary.Index > 20)
        {
            return normalized[..boundary.Index].Trim();
        }

        return normalized.Trim();
    }

    private static bool IsSpotValidityIssue(string code)
    {
        return code.Equals("missing_valid_from", StringComparison.OrdinalIgnoreCase)
            || code.Equals("missing_valid_to", StringComparison.OrdinalIgnoreCase)
            || code.Equals("invalid_valid_from", StringComparison.OrdinalIgnoreCase)
            || code.Equals("invalid_valid_to", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstText(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    private sealed record TemplateContext(
        bool IsSpot,
        DateTime Today,
        DateTime? Etd,
        string? CommodityHint
    );
}
