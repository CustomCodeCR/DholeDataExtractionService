using System.Globalization;
using System.Text.RegularExpressions;

namespace Dhole.DataExtraction.Infrastructure.Normalization;

public static class DateNormalizer
{
    private static readonly string[] Formats =
    [
        "dd/MM/yyyy",
        "MM/dd/yyyy",
        "yyyy-MM-dd",
        "dd-MM-yyyy",
        "MM-dd-yyyy",
        "dd.MM.yyyy",
        "yyyyMMdd",
        "dd/MM/yy",
        "MM/dd/yy",
        "dd-MMM-yyyy",
        "d-MMM-yyyy",
        "dd MMM yyyy",
        "d MMM yyyy",
        "MMM dd yyyy",
        "MMM d yyyy",
        "dd-MMM",
        "d-MMM",
        "dd MMM",
        "d MMM",
        "MMM-dd",
        "MMM-d",
        "MMM dd",
        "MMM d",
    ];

    private static readonly CultureInfo[] Cultures =
    [
        CultureInfo.InvariantCulture,
        new("es-CR"),
        new("es-ES"),
        new("en-US"),
    ];

    public static DateTime? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var clean = NormalizeMonthText(value.Trim());
        if (Regex.IsMatch(clean, @"^\d{1,2}$"))
        {
            return null;
        }

        var hasYear = Regex.IsMatch(clean, @"\b(?:19|20)\d{2}\b")
            || Regex.IsMatch(clean, @"\b\d{1,2}[/.-]\d{1,2}[/.-]\d{2}\b");

        foreach (var culture in Cultures)
        {
            foreach (var candidate in BuildCandidates(clean, hasYear))
            {
                if (
                    DateTime.TryParseExact(
                        candidate,
                        Formats,
                        culture,
                        DateTimeStyles.AllowWhiteSpaces,
                        out var exact
                    )
                )
                {
                    return AsUtcCommercialDate(exact);
                }

                if (
                    DateTime.TryParse(
                        candidate,
                        culture,
                        DateTimeStyles.AllowWhiteSpaces,
                        out var parsed
                    )
                )
                {
                    return AsUtcCommercialDate(parsed);
                }
            }
        }

        var serialCandidate = clean.Replace(',', '.');
        if (
            double.TryParse(
                serialCandidate,
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var serial
            )
            && serial > 1
            && serial < 60000
        )
        {
            return AsUtcCommercialDate(DateTime.FromOADate(serial));
        }

        return null;
    }

    private static DateTime AsUtcCommercialDate(DateTime value)
    {
        // Pricing validity is a calendar date, not an instant in time. Persisting a
        // Local/Unspecified midnight into PostgreSQL timestamptz can move it to the
        // previous/next day depending on the host timezone. Anchor the exact source
        // calendar day at UTC midnight so 15-Sep always remains 15-Sep end-to-end.
        return DateTime.SpecifyKind(value.Date, DateTimeKind.Utc);
    }

    private static IEnumerable<string> BuildCandidates(string value, bool hasYear)
    {
        yield return value;

        if (!hasYear)
        {
            var currentYear = DateTime.UtcNow.Year;
            yield return $"{value}-{currentYear}";
            yield return $"{value} {currentYear}";
        }
    }

    private static string NormalizeMonthText(string value)
    {
        var clean = value.Replace(".", string.Empty, StringComparison.Ordinal).Trim();
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ene"] = "Jan",
            ["enero"] = "Jan",
            ["feb"] = "Feb",
            ["febrero"] = "Feb",
            ["mar"] = "Mar",
            ["marzo"] = "Mar",
            ["abr"] = "Apr",
            ["abril"] = "Apr",
            ["may"] = "May",
            ["mayo"] = "May",
            ["jun"] = "Jun",
            ["junio"] = "Jun",
            ["jul"] = "Jul",
            ["julio"] = "Jul",
            ["ago"] = "Aug",
            ["agosto"] = "Aug",
            ["sep"] = "Sep",
            ["sept"] = "Sep",
            ["septiembre"] = "Sep",
            ["set"] = "Sep",
            ["setiembre"] = "Sep",
            ["oct"] = "Oct",
            ["octubre"] = "Oct",
            ["nov"] = "Nov",
            ["noviembre"] = "Nov",
            ["dic"] = "Dec",
            ["diciembre"] = "Dec",
        };

        foreach (var item in replacements)
        {
            clean = Regex.Replace(
                clean,
                $@"\b{Regex.Escape(item.Key)}\b",
                item.Value,
                RegexOptions.IgnoreCase
            );
        }

        return clean;
    }
}
