namespace Dhole.DataExtraction.Domain.Emails;

/// <summary>
/// Excludes automated delivery failure notifications from pricing email ingestion.
/// </summary>
public static class EmailSubjectIgnorePolicy
{
    public const string DeliveryFailureSubject = "Delivery Status Notification (Failure)";

    public static bool ShouldIgnore(string? subject) =>
        string.Equals(
            subject?.Trim(),
            DeliveryFailureSubject,
            StringComparison.OrdinalIgnoreCase
        );
}
