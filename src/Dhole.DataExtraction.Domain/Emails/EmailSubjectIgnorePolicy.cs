using System.Net;
using System.Text.RegularExpressions;

namespace Dhole.DataExtraction.Domain.Emails;

/// <summary>
/// Excludes mail-server notifications and Dhole-generated messages, including replies
/// and forwards, before they create extraction work in Pricing.
/// </summary>
public static class EmailSubjectIgnorePolicy
{
    public const string DeliveryFailureSubject = "Delivery Status Notification (Failure)";
    public const string DeliveryDelaySubject = "Delivery Status Notification (Delay)";
    public const string IgnoredNotificationReason =
        "Correo ignorado: notificación automática de estado de entrega.";
    public const string IgnoredDholeSystemReason =
        "Correo ignorado: notificación originada por Dhole System o respuesta a ella.";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);

    // Do not reject generic RE:/RV: subjects: suppliers also reply to legitimate rate requests.
    private static readonly Regex DholeNotificationSubject = new(
        @"^(?:(?:re|rv|fw|fwd)\s*:\s*)*(?:tarifa\s+lista|tarifa\s+enviada)\s*[-–—]\s*QUO-\d+-\d+\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout
    );

    // Outgoing messages use "Dhole", "Dhole Pricing", "Dhole System", etc.
    private static readonly Regex DholeSenderName = new(
        @"^dhole(?:[\s-]+[\p{L}\p{N}]+){0,4}$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout
    );

    // Outlook and Gmail quote the original From/De line; match sender identity,
    // not an incidental mention of Dhole in a supplier's rate email.
    private static readonly Regex QuotedDholeSystemSender = new(
        @"^\s*(?:>\s*)*(?:de|from|remitente)\s*:\s*[""']?\s*dhole(?:[\s-]+[\p{L}\p{N}]+){0,4}\b",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant,
        RegexTimeout
    );

    private static readonly Regex QuotedDholeAttribution = new(
        @"^\s*(?:>\s*)*(?:on|el)\s+[^\r\n]{0,240}\bdhole(?:[\s-]+[\p{L}\p{N}]+){0,4}\s*(?:<[^>\r\n]+>)?\s*(?:wrote|escribi[oó])\s*:",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant,
        RegexTimeout
    );

    // A reply carrying the link to an original Dhole quotation is a system
    // conversation even when the mail client removes the original From line.
    private static readonly Regex DholePricingLink = new(
        @"https?://sistema\.logisticacastrofallas\.com/pricing/(?:rates|seller-rates)/[0-9a-f-]{16,}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout
    );

    private static readonly Regex ReplyOrForwardPrefix = new(
        @"^(?:(?:re|rv|fw|fwd|aw)\s*:\s*)+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout
    );

    private static readonly Regex HtmlLineBreaks = new(
        @"</?(?:br|div|p|li|tr|table|blockquote|section|header)\b[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout
    );

    private static readonly Regex HtmlTags = new(
        @"<[^>]*>",
        RegexOptions.Singleline,
        RegexTimeout
    );

    public static bool ShouldIgnore(
        string? subject,
        string? fromAddress = null,
        string? fromName = null,
        string? bodyText = null,
        string? bodyHtml = null
    ) => GetIgnoreReason(subject, fromAddress, fromName, bodyText, bodyHtml) is not null;

    public static string? GetIgnoreReason(
        string? subject,
        string? fromAddress = null,
        string? fromName = null,
        string? bodyText = null,
        string? bodyHtml = null
    )
    {
        var normalizedSubject = subject?.Trim();
        if (string.Equals(normalizedSubject, DeliveryFailureSubject, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedSubject, DeliveryDelaySubject, StringComparison.OrdinalIgnoreCase))
        {
            return IgnoredNotificationReason;
        }

        var normalizedSender = fromAddress?.Trim();
        if (normalizedSender?.StartsWith("mailer-daemon@", StringComparison.OrdinalIgnoreCase) == true
            || normalizedSender?.StartsWith("postmaster@", StringComparison.OrdinalIgnoreCase) == true)
        {
            return IgnoredNotificationReason;
        }

        var senderName = Regex.Replace(fromName?.Trim() ?? string.Empty, @"\s+", " ");
        var htmlAsText = AsPlainText(bodyHtml);

        if (DholeSenderName.IsMatch(senderName)
            || (!string.IsNullOrWhiteSpace(normalizedSubject)
                && DholeNotificationSubject.IsMatch(normalizedSubject))
            || ContainsQuotedDholeSender(bodyText)
            || ContainsQuotedDholeSender(htmlAsText)
            || (ReplyOrForwardPrefix.IsMatch(normalizedSubject ?? string.Empty)
                && (ContainsDholePricingLink(bodyText) || ContainsDholePricingLink(bodyHtml))))
        {
            return IgnoredDholeSystemReason;
        }

        return null;
    }

    private static bool ContainsQuotedDholeSender(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Replace('\u00a0', ' ');
        return QuotedDholeSystemSender.IsMatch(normalized)
            || QuotedDholeAttribution.IsMatch(normalized);
    }

    private static bool ContainsDholePricingLink(string? text) =>
        !string.IsNullOrWhiteSpace(text) && DholePricingLink.IsMatch(text);

    private static string? AsPlainText(string? html)
    {
        return string.IsNullOrWhiteSpace(html)
            ? null
            : WebUtility.HtmlDecode(HtmlTags.Replace(HtmlLineBreaks.Replace(html, "\n"), " "));
    }
}
