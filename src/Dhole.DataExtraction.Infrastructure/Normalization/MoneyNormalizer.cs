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

        var negativeByParentheses = text.StartsWith('(') && text.EndsWith(')');
        var cleaned = Regex.Replace(text, @"[^\d\.,\-]", "");

        if (string.IsNullOrWhiteSpace(cleaned) || cleaned is "-" or "." or ",")
        {
            return null;
        }

        cleaned = NormalizeSeparators(cleaned);

        if (negativeByParentheses && !cleaned.StartsWith('-'))
        {
            cleaned = $"-{cleaned}";
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

    private static string NormalizeSeparators(string value)
    {
        var cleaned = value;
        var lastComma = cleaned.LastIndexOf(',');
        var lastDot = cleaned.LastIndexOf('.');

        if (lastComma >= 0 && lastDot >= 0)
        {
            var decimalSeparator = lastComma > lastDot ? ',' : '.';
            var thousandSeparator = decimalSeparator == ',' ? "." : ",";

            cleaned = cleaned.Replace(thousandSeparator, string.Empty);
            cleaned = decimalSeparator == ',' ? cleaned.Replace(',', '.') : cleaned;
            return cleaned;
        }

        if (lastComma >= 0)
        {
            return NormalizeSingleSeparator(cleaned, ',');
        }

        if (lastDot >= 0)
        {
            return NormalizeSingleSeparator(cleaned, '.');
        }

        return cleaned;
    }

    private static string NormalizeSingleSeparator(string value, char separator)
    {
        var separatorCount = value.Count(ch => ch == separator);
        var lastIndex = value.LastIndexOf(separator);
        var digitsAfter = value.Length - lastIndex - 1;

        if (separatorCount > 1)
        {
            return value.Replace(separator.ToString(), string.Empty);
        }

        if (digitsAfter == 3)
        {
            return value.Replace(separator.ToString(), string.Empty);
        }

        return separator == ',' ? value.Replace(',', '.') : value;
    }
}
