namespace Dhole.DataExtraction.Domain.Emails;

/// <summary>
/// Excludes automated delivery status messages and mail-server bounces from pricing ingestion.
/// </summary>
public static class EmailSubjectIgnorePolicy
{
    public const string DeliveryFailureSubject = "Delivery Status Notification (Failure)";
    public const string DeliveryDelaySubject = "Delivery Status Notification (Delay)";
    public const string IgnoredNotificationReason =
        "Correo ignorado: notificación automática de estado de entrega.";

    public static bool ShouldIgnore(string? subject, string? fromAddress = null)
    {
        var normalizedSubject = subject?.Trim();
        if (
            string.Equals(normalizedSubject, DeliveryFailureSubject, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedSubject, DeliveryDelaySubject, StringComparison.OrdinalIgnoreCase)
        )
        {
            return true;
        }

        // Providers may send bounces with a different subject. Never treat the
        // original tariff quoted inside a mailer-daemon/postmaster reply as a new rate.
        var normalizedSender = fromAddress?.Trim();
        return normalizedSender?.StartsWith("mailer-daemon@", StringComparison.OrdinalIgnoreCase) == true
            || normalizedSender?.StartsWith("postmaster@", StringComparison.OrdinalIgnoreCase) == true;
    }
}
