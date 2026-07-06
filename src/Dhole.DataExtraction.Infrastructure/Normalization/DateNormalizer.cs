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
        var hasYear = Regex.IsMatch(clean, @"\b(?:19|20)\d{2}\b") || Regex.IsMatch(clean, @"\b\d{1,2}[/.-]\d{1,2}[/.-]\d{2}\b");

        foreach (var culture in Cultures)
        {
            foreach (var candidate in BuildCandidates(clean, hasYear))
            {
                if (DateTime.TryParseExact(candidate, Formats, culture, DateTimeStyles.AssumeLocal, out var exact))
                {
                    return exact.Date;
                }

                if (DateTime.TryParse(candidate, culture, DateTimeStyles.AssumeLocal, out var parsed))
                {
                    return parsed.Date;
                }
            }
        }

        var serialCandidate = clean.Replace(',', '.');
        if (double.TryParse(serialCandidate, NumberStyles.Number, CultureInfo.InvariantCulture, out var serial)
            && serial > 1
            && serial < 60000)
        {
            return DateTime.FromOADate(serial).Date;
        }

        return null;
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
            clean = Regex.Replace(clean, $@"\b{Regex.Escape(item.Key)}\b", item.Value, RegexOptions.IgnoreCase);
        }

        return clean;
    }
}
