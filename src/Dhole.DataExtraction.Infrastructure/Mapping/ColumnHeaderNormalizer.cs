using System.Globalization;
using System.Text;

namespace Dhole.DataExtraction.Infrastructure.Mapping;

public static class ColumnHeaderNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim().ToLowerInvariant();
        normalized = RemoveDiacritics(normalized);

        normalized = normalized
            .Replace(" ", "")
            .Replace("_", "")
            .Replace("-", "")
            .Replace(".", "")
            .Replace("/", "")
            .Replace("\\", "")
            .Replace("(", "")
            .Replace(")", "");

        return normalized;
    }

    private static string RemoveDiacritics(string text)
    {
        var normalized = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder();

        foreach (var character in normalized)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(character);

            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
