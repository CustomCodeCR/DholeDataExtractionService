using System.Globalization;

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
    ];

    public static DateTime? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var clean = value.Trim();

        if (
            DateTime.TryParseExact(
                clean,
                Formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var exact
            )
        )
        {
            return exact.Date;
        }

        return DateTime.TryParse(clean, out var parsed) ? parsed.Date : null;
    }
}
