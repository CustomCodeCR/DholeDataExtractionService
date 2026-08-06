using System.Globalization;
using System.Text.RegularExpressions;

namespace Dhole.DataExtraction.Infrastructure.Normalization;

public static class MoneyNormalizer
{
    // PostgreSQL numeric(18,4) accepts at most 14 integer digits. Keeping the
    // same boundary here prevents a malformed PDF cell from reaching EF/Npgsql
    // as a value that can only fail during SaveChanges with SQLSTATE 22003.
    private const decimal MaximumNumeric18Scale4 = 99_999_999_999_999.9999m;

    private static readonly Regex CurrencyAmountRegex = new(
        @"(?:\b(?:USD|EUR|CRC|GBP|JPY|CNY|RMB|CAD|MXN|COP|PAB)\b|US\$|[$€₡£])\s*(?<number>\(?-?(?:\d{1,3}(?:[\s\u00A0.,]\d{3})+(?:[.,]\d+)?|\d+(?:[.,]\d+)?)\)?)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex StandaloneAmountRegex = new(
        @"(?<!\d)(?<number>\(?-?(?:\d{1,3}(?:[\s\u00A0.,]\d{3})+(?:[.,]\d+)?|\d+(?:[.,]\d+)?)\)?)(?!\d)",
        RegexOptions.Compiled
    );

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

        // Do not concatenate every digit present in a descriptive cell. Values such as
        // "USD 200/20'" or a PDF cell accidentally containing a date used to become
        // 20020 or a much larger number. Select one monetary token first, preferring the
        // value that follows an explicit currency marker.
        var token = ExtractPrimaryAmountToken(text);
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var negativeByParentheses = token.StartsWith('(') && token.EndsWith(')');
        var cleaned = Regex.Replace(token, @"[^\d\.,\-]", "");

        if (string.IsNullOrWhiteSpace(cleaned) || cleaned is "-" or "." or ",")
        {
            return null;
        }

        cleaned = NormalizeSeparators(cleaned);

        if (negativeByParentheses && !cleaned.StartsWith('-'))
        {
            cleaned = $"-{cleaned}";
        }

        if (!decimal.TryParse(
            cleaned,
            NumberStyles.Number | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture,
            out var result
        ))
        {
            return null;
        }

        return ToNumeric18Scale4(result);
    }

    public static decimal? ToNumeric18Scale4(decimal? value)
    {
        if (!value.HasValue || Math.Abs(value.Value) > MaximumNumeric18Scale4)
        {
            return null;
        }

        return decimal.Round(value.Value, 4, MidpointRounding.AwayFromZero);
    }

    private static string? ExtractPrimaryAmountToken(string value)
    {
        var currencyMatch = CurrencyAmountRegex.Match(value);
        if (currencyMatch.Success)
        {
            return currencyMatch.Groups["number"].Value;
        }

        var standaloneMatch = StandaloneAmountRegex.Match(value);
        return standaloneMatch.Success
            ? standaloneMatch.Groups["number"].Value
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
