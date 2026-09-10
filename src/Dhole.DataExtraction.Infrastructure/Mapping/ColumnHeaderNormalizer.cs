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

        var normalized = RemoveDiacritics(value.Trim().ToLowerInvariant());
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        var compact = builder.ToString();

        // The approved pricing spreadsheet uses business-friendly Spanish headers.
        // Canonicalize those headers to keys already understood by the deterministic
        // FCL mapping so a complete XLSX can be consumed without an AI round-trip.
        return compact switch
        {
            "validodesde" => "validfrom",
            "validohasta" => "validto",
            "fleteinternacional" => "oceanfreight",
            "tiempotransitodias" => "transitdays",
            "diaslibresendestino" => "freedays",
            _ => compact,
        };
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
