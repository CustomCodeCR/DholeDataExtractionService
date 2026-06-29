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

        var text = value.Trim();

        if (text is "-" or "--" or "$ -" or "$-" or "N/A" or "n/a")
        {
            return null;
        }

        var cleaned = Regex.Replace(text, @"[^\d\.,\-]", "");

        if (string.IsNullOrWhiteSpace(cleaned) || cleaned is "-" or "." or ",")
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
