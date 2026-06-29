using System.Globalization;
using System.Text.RegularExpressions;

namespace Dhole.DataExtraction.Infrastructure.Normalization;

public static class MoneyNormalizer
{
    public static decimal? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = Regex.Replace(value, @"[^\d\.,\-]", "");

        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        if (cleaned.Contains(',') && cleaned.Contains('.'))
        {
            cleaned = cleaned.Replace(",", "");
        }
        else if (cleaned.Contains(',') && !cleaned.Contains('.'))
        {
            cleaned = cleaned.Replace(",", ".");
        }

        return decimal.TryParse(
            cleaned,
            NumberStyles.Number | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var result
        )
            ? result
            : null;
    }
}
