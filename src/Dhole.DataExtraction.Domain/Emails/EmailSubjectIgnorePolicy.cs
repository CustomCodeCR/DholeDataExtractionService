using System.Net;
using System.Text.RegularExpressions;

namespace Dhole.DataExtraction.Domain.Emails;

/// <summary>
/// Excludes mail-server notifications and Dhole-generated emails (including replies
/// and forwards of those emails) before pricing ingestion or manual reprocessing.
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

    // The quote number makes this a Dhole notification, not a supplier's generic
    // "tarifa enviada/lista" conversation. Reply/forward prefixes are optional.
    private static readonly Regex DholeNotificationSubject = new(
        @"^(?:(?:re|rv|fw|fwd)\s*:\s*)*(?:tarifa\s+lista|tarifa\s+enviada)\s*[-–—]\s*QUO-\d+-\d+\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        RegexTimeout
    );

    // Outlook/Gmail include the original sender in quoted/forwarded headers.
    // Match the sender of the quoted message, not a passing mention of "Dhole".
    private static readonly Regex QuotedDholeSystemSender = new(
        @"^\s*(?:>\s*)*(?:de|from|remitente)\s*:\s*[""']?\s*dhole\s+system\b",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant,
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

        // Original rate quotations quoted by the mail server must not be processed.
        var normalizedSender = fromAddress?.Trim();
        if (normalizedSender?.StartsWith("mailer-daemon@", StringComparison.OrdinalIgnoreCase) == true
            || normalizedSender?.StartsWith("postmaster@", StringComparison.OrdinalIgnoreCase) == true)
        {
            return IgnoredNotificationReason;
        }

        var senderName = Regex.Replace(fromName?.Trim() ?? string.Empty, @"\s+", " ");
        if (string.Equals(senderName, "Dhole System", StringComparison.OrdinalIgnoreCase)
            || string.Equals(senderName, "Dhole", StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(normalizedSubject)
                && DholeNotificationSubject.IsMatch(normalizedSubject))
            || ContainsQuotedDholeSender(bodyText)
            || ContainsQuotedDholeSender(AsPlainText(bodyHtml)))
        {
            return IgnoredDholeSystemReason;
        }

        return null;
    }

    private static bool ContainsQuotedDholeSender(string? text)
    {
        return !string.IsNullOrWhiteSpace(text)
            && QuotedDholeSystemSender.IsMatch(text.Replace('\u00a0', ' '));
    }

    private static string? AsPlainText(string? html)
    {
        return string.IsNullOrWhiteSpace(html)
            ? null
            : WebUtility.HtmlDecode(HtmlTags.Replace(html, " "));
    }
}
