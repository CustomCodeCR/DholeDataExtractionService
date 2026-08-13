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
    private const int MinimumStrongPricingSectionScore = 45;

    private static readonly Regex PricingStartRegex = new(
        @"\b(?:pls|please)\s+consider\s+(?:the\s+)?rate\b"
            + @"|\bpublished\s+fak\b"
            + @"|\b(?:pls|please)\s+(?:check|see|find)\s+(?:the\s+)?(?:below\s+)?(?:the\s+)?(?:updat(?:e|ed)\s+)?rates?\b"
            + @"|\bupdat(?:e|ed)\s+rates?\s+for\s+(?:your\s+)?ref(?:erence)?\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex RateSignalRegex = new(
        @"\b(?:USD|EUR|CRC|US\$)\s*\d|\$\s*\d|\b(?:POL|POD|POE|COMM(?:ODITY)?|CARRIER)\s*:",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex CurrencyAmountRegex = new(
        @"(?:(?:\b(?:USD|EUR|CRC)\b|\bUS\$)[^\d\r\n]{0,12}|[$€₡]\s*)\d[\d\s.,]*|\b(?:freight\s*amount|ocean\s*freight|rate\s*amount|flete)\s*[:=]\s*\d[\d\s.,]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex StructuralLineRegex = new(
        @"^(?:[A-Z]\)|POL\s*:|POD\s*:|POE\s*:|COMM(?:ODITY)?\s*:|If\s+big\s+lot\b|BUT\b|Subject\s+to\b|If\s+space\b|Below\s+the\s+details\b|Pls\s+note\b|Please\s+note\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    private static readonly Regex ThreadHeaderRegex = new(
        @"^(?:De|From|发件人|Von|Da|Remitente|·¢¼þÈË)\s*:"
            + @"|^(?:Enviado|Sent|发送时间|Date|Fecha|·¢ËÍÊ±¼ä)\s*:"
            + @"|^(?:Asunto|Subject|主题|Ö÷Ìâ)\s*:"
            + @"|^On\s+.+\bwrote\s*:$|^-{2,}\s*Original Message\s*-{2,}$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled
    );

    public static string SelectPreferredBody(string? bodyText, string? bodyHtml)
    {
        var text = NormalizePlainText(bodyText);
        var html = NormalizeHtml(bodyHtml);

        // Score the focused sections, not the complete representations. Outlook can
        // expose a short current offer in HTML while the plain-text alternative contains
        // a much longer quoted history; historical volume must never decide which body wins.
        var focusedText = SelectBestPricingSection(text);
        var focusedHtml = SelectBestPricingSection(html);
        var textScore = ScorePricingContent(focusedText);
        var htmlScore = ScorePricingContent(focusedHtml);

        // WWL and other forwarders often send a useful one-cell-per-line plain-text
        // matrix plus an HTML alternative whose table cells collapse into long lines
        // after normalization. Prefer the plain-text representation whenever it keeps
        // the stacked FCL header shape; the deterministic parser can reconstruct it
        // without AI and preserve Validity (ETD) exactly.
        if (HasStackedFclShape(focusedText))
        {
            return focusedText;
        }

        return textScore > 0 && textScore >= htmlScore
            ? focusedText
            : htmlScore > 0
                ? focusedHtml
                : FirstNotEmpty(focusedText, focusedHtml, text, html) ?? string.Empty;
    }

    private static bool HasStackedFclShape(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var lines = NormalizePlainText(value)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(CleanLine)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].Equals("POL", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var lookAhead = lines.Skip(index).Take(12).ToArray();
            var hasDestination = lookAhead.Any(line =>
                line.Equals("POD", StringComparison.OrdinalIgnoreCase)
                || line.Equals("POE", StringComparison.OrdinalIgnoreCase)
            );
            var hasCarrier = lookAhead.Any(line =>
                line.Equals("CARRIER", StringComparison.OrdinalIgnoreCase)
                || line.Equals("NAVIERA", StringComparison.OrdinalIgnoreCase)
            );
            var hasValidity = lookAhead.Any(line =>
                line.Contains("Validity", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Effective", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Expiry", StringComparison.OrdinalIgnoreCase)
                || line.Contains("Vigencia", StringComparison.OrdinalIgnoreCase)
            );
            var hasEquipment = lookAhead.Any(line =>
                Regex.IsMatch(line, @"^(?:20|40|45)\s*['’]?", RegexOptions.IgnoreCase)
            );

            if (hasDestination && hasCarrier && hasValidity && hasEquipment)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns only the currently authored message, excluding quoted reply/forward
    /// history. This is used when a supported tariff attachment exists so an old rate
    /// contained in the thread cannot create a second body import.
    /// </summary>
    public static string SelectCurrentMessageBody(string? bodyText, string? bodyHtml)
    {
        var text = SelectCurrentMessageSection(NormalizePlainText(bodyText));
        var html = SelectCurrentMessageSection(NormalizeHtml(bodyHtml));

        var textScore = ScorePricingContent(text);
        var htmlScore = ScorePricingContent(html);
        return textScore > 0 && textScore >= htmlScore
            ? text
            : htmlScore > 0
                ? html
                : FirstNotEmpty(text, html) ?? string.Empty;
    }

    public static string SelectCurrentMessageSection(string? value)
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

        var sections = SplitMessageSections(lines);
        var current = sections.FirstOrDefault(section => section.Count > 0) ?? lines;
        return ReconstructLogicalLines(current);
    }

    /// <summary>
    /// Splits a reply/forward chain into logical messages and selects the first section
    /// that actually contains a rate amount plus sufficient route/equipment/validity
    /// structure. This prevents signatures and quoted requests such as "Adjunto tarifas"
    /// from being sent to Pricing as an empty pseudo-rate when a real attachment exists.
    /// </summary>
    public static string SelectBestPricingSection(string? value)
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

        var sections = SplitMessageSections(lines);

        // Sections are ordered newest-to-oldest as they appear in the email body.
        // Always take the first section that contains a complete tariff. Choosing the
        // highest score is incorrect for reply chains because an older message with
        // more rows/amounts can otherwise displace the current offer.
        var newestStrong = sections
            .Select(section => new
            {
                Section = section,
                Score = ScorePricingContent(string.Join('\n', section)),
            })
            .FirstOrDefault(candidate => candidate.Score >= MinimumStrongPricingSectionScore);

        if (newestStrong is not null)
        {
            return SelectNewestPricingSection(string.Join('\n', newestStrong.Section));
        }

        // No section contains actual rate data. Keep only the newest visible message,
        // rather than forwarding the whole historical chain to the extractor/AI.
        var newest = sections.FirstOrDefault(section => section.Count > 0) ?? lines;
        return ReconstructLogicalLines(newest);
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

        var contextualStart = FindCommercialContextStart(lines, start);
        var end = FindMessageEnd(lines, start + 1);
        var relevant = lines[contextualStart..end];
        return ReconstructLogicalLines(relevant);
    }

    private static int FindCommercialContextStart(
        IReadOnlyList<string> lines,
        int pricingStart
    )
    {
        var start = pricingStart;
        var minimum = Math.Max(0, pricingStart - 3);

        for (var index = pricingStart - 1; index >= minimum; index--)
        {
            var line = lines[index];
            if (
                Regex.IsMatch(
                    line,
                    @"\bspace\s+is\s+tight\b|\bspace\s+availability\b|\bavailability\b.{0,80}\bconfirm(?:ed|ation)?\b|\brollovers?\b",
                    RegexOptions.IgnoreCase
                )
            )
            {
                start = index;
                continue;
            }

            break;
        }

        return start;
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

    private static int ScorePricingContent(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        var amountCount = CurrencyAmountRegex.Matches(value).Count;
        var hasOrigin = Regex.IsMatch(
            value,
            @"\bPOL\b|\bPuerto\s+de\s+Origen\b|\bOrigin\s+Port\b|\bPort\s+of\s+Loading\b",
            RegexOptions.IgnoreCase
        );
        var hasDestination = Regex.IsMatch(
            value,
            @"\bPOD\b|\bPOE\b|\bPuerto\s+Destino\b|\bDestination\s+Port\b|\bPort\s+of\s+Discharge\b",
            RegexOptions.IgnoreCase
        );
        var hasCarrier = Regex.IsMatch(
            value,
            @"\bCarrier\b|\bNaviera\b|\bShipping\s+Line\b",
            RegexOptions.IgnoreCase
        );
        var hasEquipment = Regex.IsMatch(
            value,
            @"\b(?:20|40|45)\s*['’]?\s*(?:GP|DV|DC|STD|ST|HC|HQ|NOR|RF)?\b",
            RegexOptions.IgnoreCase
        );
        var hasValidity = Regex.IsMatch(
            value,
            @"\b(?:validity|valid\s+from|valid\s+to|effective\s+date|effective\s+etd|expiry\s+date|vigencia|vencimiento|vence)\b|\b\d{1,2}[/.-]\d{1,2}[/.-]\d{2,4}\s+(?:AL|TO|A)\s+\d{1,2}[/.-]\d{1,2}[/.-]\d{2,4}\b",
            RegexOptions.IgnoreCase
        );
        var hasNarrativeRate = PricingStartRegex.IsMatch(value)
            || Regex.IsMatch(value, @"\brate\s+(?:USD|EUR|CRC|US\$|[$€₡])", RegexOptions.IgnoreCase);

        var score = PricingStartRegex.Matches(value).Count * 120;
        score += Math.Min(amountCount, 12) * 8;
        score += hasOrigin ? 14 : 0;
        score += hasDestination ? 14 : 0;
        score += hasCarrier ? 8 : 0;
        score += hasEquipment ? 10 : 0;
        score += hasValidity ? 10 : 0;
        score += hasNarrativeRate ? 20 : 0;
        score += Regex.Matches(value, @"\bPOL\s*:", RegexOptions.IgnoreCase).Count * 8;
        score += Regex.Matches(value, @"\bPOD\s*:", RegexOptions.IgnoreCase).Count * 8;
        score += Regex.Matches(value, @"\bCOMM(?:ODITY)?\s*:", RegexOptions.IgnoreCase).Count * 6;

        // A quoted request can mention POL/POD/equipment and dates, but without a
        // currency amount it is not an actual tariff and must not create an import.
        if (amountCount == 0)
        {
            score = Math.Min(score, 35);
        }

        return score;
    }

    private static IReadOnlyList<IReadOnlyList<string>> SplitMessageSections(
        IReadOnlyList<string> lines
    )
    {
        var sections = new List<IReadOnlyList<string>>();
        var current = new List<string>();

        foreach (var line in lines)
        {
            if (IsStrongSeparator(line))
            {
                Flush();
                continue;
            }

            if (ThreadHeaderRegex.IsMatch(line) && current.Count > 0)
            {
                Flush();
            }

            current.Add(line);
        }

        Flush();
        if (sections.Count == 0)
        {
            sections.Add(lines.ToArray());
        }

        return sections;

        void Flush()
        {
            if (current.Count == 0)
            {
                return;
            }

            sections.Add(current.ToArray());
            current = new List<string>();
        }
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

            if (IsStackedTariffHeader(lines, index))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsStackedTariffHeader(IReadOnlyList<string> lines, int index)
    {
        if (!lines[index].Equals("POL", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var lookAhead = lines
            .Skip(index)
            .Take(12)
            .Select(line => line.Trim())
            .ToArray();

        var hasDestination = lookAhead.Any(line =>
            line.Equals("POD", StringComparison.OrdinalIgnoreCase)
            || line.Equals("POE", StringComparison.OrdinalIgnoreCase)
            || line.Contains("DESTINATION", StringComparison.OrdinalIgnoreCase)
        );
        var hasCarrier = lookAhead.Any(line =>
            line.Equals("CARRIER", StringComparison.OrdinalIgnoreCase)
            || line.Equals("NAVIERA", StringComparison.OrdinalIgnoreCase)
        );
        var hasEquipment = lookAhead.Any(line =>
            Regex.IsMatch(line, @"^(?:20|40|45)\s*['’]?", RegexOptions.IgnoreCase)
        );
        var hasValidity = lookAhead.Any(line =>
            Regex.IsMatch(
                line,
                @"^(?:effective|expiry|validity|valid\s+from|valid\s+to|vigencia|vencimiento)",
                RegexOptions.IgnoreCase
            )
        );

        return hasDestination && hasCarrier && hasEquipment && hasValidity;
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
                || line.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Asunto:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("·¢¼þÈË:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("·¢ËÍÊ±¼ä:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Ö÷Ìâ:", StringComparison.OrdinalIgnoreCase)
                || IsStrongSeparator(line)
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
            || line.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Asunto:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("·¢¼þÈË:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("·¢ËÍÊ±¼ä:", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Ö÷Ìâ:", StringComparison.OrdinalIgnoreCase)
            || IsStrongSeparator(line);
    }

    private static bool IsStrongSeparator(string line)
    {
        return Regex.IsMatch(line, @"^[_=-]{3,}$");
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
