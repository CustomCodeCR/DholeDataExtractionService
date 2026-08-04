using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Dhole.DataExtraction.Infrastructure.Email;

/// <summary>
/// Selects the newest pricing-bearing message from forwarded/replied email threads.
/// Outlook commonly places a corporate signature before the forwarded rate and wraps
/// one logical sentence across several HTML block elements. This helper converts that
/// content into a compact, deterministic representation shared by extraction and AI.
/// </summary>
public static class EmailPricingContentSelector
{
    private static readonly Regex PricingStartRegex = new(
        @"\b(?:pls|please)\s+consider\s+(?:the\s+)?rate\b|\bpublished\s+fak\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex RateSignalRegex = new(
        @"\b(?:USD|EUR|CRC|US\$)\s*\d|\$\s*\d|\b(?:POL|POD|POE|COMM(?:ODITY)?|CARRIER)\s*:",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex StructuralLineRegex = new(
        @"^(?:[A-Z]\)|POL\s*:|POD\s*:|POE\s*:|COMM(?:ODITY)?\s*:|If\s+big\s+lot\b|BUT\b|Subject\s+to\b|If\s+space\b|Below\s+the\s+details\b|Pls\s+note\b|Please\s+note\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    public static string SelectPreferredBody(string? bodyText, string? bodyHtml)
    {
        var text = NormalizePlainText(bodyText);
        var html = NormalizeHtml(bodyHtml);

        var textScore = Score(text);
        var htmlScore = Score(html);
        var selected = PricingStartRegex.IsMatch(text)
            ? text
            : PricingStartRegex.IsMatch(html)
                ? html
                : textScore > 0 && textScore >= htmlScore
                    ? text
                    : htmlScore > 0
                        ? html
                        : FirstNotEmpty(text, html) ?? string.Empty;

        return SelectNewestPricingSection(selected);
    }

    public static string SelectNewestPricingSection(string? value)
    {
        var normalized = NormalizePlainText(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var lines = normalized
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CleanLine)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        if (lines.Length == 0)
        {
            return string.Empty;
        }

        var start = FindPricingStart(lines);
        if (start < 0)
        {
            return string.Join('\n', lines);
        }

        var end = FindMessageEnd(lines, start + 1);
        var relevant = lines[start..end];
        return ReconstructLogicalLines(relevant);
    }

    public static string NormalizeHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var value = Regex.Replace(
            html,
            @"<(script|style)[^>]*>.*?</\1>",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );
        value = Regex.Replace(value, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"</(td|th)>", "\t", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, @"</tr>", "\n", RegexOptions.IgnoreCase);
        value = Regex.Replace(
            value,
            @"</(p|div|li|h[1-6])>",
            "\n",
            RegexOptions.IgnoreCase
        );
        value = Regex.Replace(value, @"<[^>]+>", " ");
        return NormalizePlainText(WebUtility.HtmlDecode(value));
    }

    private static int Score(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var score = PricingStartRegex.Matches(value).Count * 100;
        score += Regex.Matches(value, @"\bPOL\s*:", RegexOptions.IgnoreCase).Count * 8;
        score += Regex.Matches(value, @"\bPOD\s*:", RegexOptions.IgnoreCase).Count * 8;
        score += Regex.Matches(value, @"\bCOMM(?:ODITY)?\s*:", RegexOptions.IgnoreCase).Count * 6;
        score += Regex.Matches(value, @"\bCarrier\b", RegexOptions.IgnoreCase).Count * 2;
        return score;
    }

    private static int FindPricingStart(IReadOnlyList<string> lines)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (PricingStartRegex.IsMatch(lines[index]))
            {
                return index;
            }

            if (
                lines[index].Equals("FAK", StringComparison.OrdinalIgnoreCase)
                && lines.Skip(index + 1).Take(8).Any(line =>
                    line.Equals("POL", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("POL:", StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindMessageEnd(IReadOnlyList<string> lines, int start)
    {
        for (var index = start; index < lines.Count; index++)
        {
            var line = lines[index].Trim();
            if (
                line.StartsWith("Un saludo", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Regards", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Best regards", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Kind regards", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Worldwide Logistics", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("发件人:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("De:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("From:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Sent:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Enviado:", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(line, @"^[_=-]{8,}$")
            )
            {
                return index;
            }
        }

        return lines.Count;
    }

    private static string ReconstructLogicalLines(IReadOnlyList<string> lines)
    {
        var result = new List<string>();

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];

            if (PricingStartRegex.IsMatch(line))
            {
                var builder = new StringBuilder(line);
                while (index + 1 < lines.Count && IsRateOfferContinuation(lines[index + 1]))
                {
                    Append(builder, lines[++index]);
                }

                result.Add(CollapseWhitespace(builder.ToString()));
                continue;
            }

            if (Regex.IsMatch(line, @"^(?:POL|POD|POE|COMM(?:ODITY)?)\s*:", RegexOptions.IgnoreCase))
            {
                var builder = new StringBuilder(line);
                while (
                    index + 1 < lines.Count
                    && IsKeyValueContinuation(builder.ToString(), lines[index + 1])
                )
                {
                    Append(builder, lines[++index]);
                }

                result.Add(CollapseWhitespace(builder.ToString()));
                continue;
            }

            result.Add(CollapseWhitespace(line));
        }

        return string.Join('\n', result.Where(line => !string.IsNullOrWhiteSpace(line)));
    }

    private static bool IsRateOfferContinuation(string nextLine)
    {
        if (StructuralLineRegex.IsMatch(nextLine))
        {
            return false;
        }

        return nextLine == ","
            || nextLine.StartsWith("valid ", StringComparison.OrdinalIgnoreCase)
            || nextLine.StartsWith("Carrier ", StringComparison.OrdinalIgnoreCase)
            || nextLine.StartsWith("with ", StringComparison.OrdinalIgnoreCase)
            || nextLine.StartsWith("per ", StringComparison.OrdinalIgnoreCase)
            || nextLine.StartsWith("(", StringComparison.Ordinal)
            || !RateSignalRegex.IsMatch(nextLine) && nextLine.Length <= 90;
    }

    private static bool IsKeyValueContinuation(string current, string nextLine)
    {
        if (StructuralLineRegex.IsMatch(nextLine) || IsMessageBoundary(nextLine))
        {
            return false;
        }

        var hasOpenParenthesis = current.Count(character => character == '(')
            > current.Count(character => character == ')');
        return hasOpenParenthesis
            || nextLine.StartsWith("/", StringComparison.Ordinal)
            || Regex.IsMatch(nextLine, @"^(?:USD|EUR|CRC|US\$|\$)\s*\d", RegexOptions.IgnoreCase)
            || current.EndsWith("/", StringComparison.Ordinal)
            || current.EndsWith(",", StringComparison.Ordinal);
    }

    private static bool IsMessageBoundary(string line)
    {
        return line.StartsWith("Un saludo", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Regards", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("发件人:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("De:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("From:", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(line, @"^[_=-]{8,}$");
    }

    private static string NormalizePlainText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\u00A0", " ", StringComparison.Ordinal);
        normalized = Regex.Replace(normalized, @"[ \t]+(?=\n|$)", string.Empty);
        normalized = Regex.Replace(normalized, @"\n{3,}", "\n\n");
        return normalized.Trim();
    }

    private static string CleanLine(string value)
    {
        return CollapseWhitespace(value.Trim().TrimStart('>', '|', '-', '*').Trim());
    }

    private static string CollapseWhitespace(string value)
    {
        return Regex.Replace(value, @"[ \t]+", " ").Trim();
    }

    private static void Append(StringBuilder builder, string value)
    {
        var clean = CollapseWhitespace(value).TrimStart(',');
        if (string.IsNullOrWhiteSpace(clean))
        {
            return;
        }

        if (builder.Length > 0 && builder[^1] is not ' ' and not '/')
        {
            builder.Append(' ');
        }

        builder.Append(clean);
    }

    private static string? FirstNotEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
