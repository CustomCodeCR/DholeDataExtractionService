using System.Net;
using System.Text.RegularExpressions;
using Dhole.DataExtraction.Infrastructure.Files;

namespace Dhole.DataExtraction.Infrastructure.Email;

public static partial class EmailSubjectNormalizer
{
    public static string NormalizeForExtraction(string? subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return string.Empty;
        }

        var value = WebUtility.HtmlDecode(TextContentDecoder.Clean(subject)).Trim();
        while (ReplyPrefixRegex().IsMatch(value))
        {
            value = ReplyPrefixRegex().Replace(value, string.Empty, 1).TrimStart();
        }

        value = SeparatorRegex().Replace(value, " | ");
        value = WhitespaceRegex().Replace(value, " ");
        value = PipeWhitespaceRegex().Replace(value, " | ");
        return value.Trim(' ', '|', '-', ':', ';');
    }

    [GeneratedRegex(@"^(?:(?:RE|RV|FW|FWD|ENC|TR|WG|AW)\s*:\s*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReplyPrefixRegex();

    [GeneratedRegex(@"\s*(?://+|\\+|\|+|;+|\s/\s)\s*", RegexOptions.CultureInvariant)]
    private static partial Regex SeparatorRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"\s*\|\s*", RegexOptions.CultureInvariant)]
    private static partial Regex PipeWhitespaceRegex();
}
