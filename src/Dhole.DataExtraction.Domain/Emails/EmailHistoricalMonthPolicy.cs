namespace Dhole.DataExtraction.Domain.Emails;

/// <summary>
/// Dhole's operating month is based on the Costa Rican calendar, and the original
/// email received timestamp rather than the creation date of a retry.
/// </summary>
public static class EmailHistoricalMonthPolicy
{
    private static readonly TimeZoneInfo CostaRica =
        TimeZoneInfo.FindSystemTimeZoneById("America/Costa_Rica");

    public static DateTime CurrentMonthStartUtc(DateTime nowUtc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(
            nowUtc.Kind == DateTimeKind.Utc
                ? nowUtc
                : DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc),
            CostaRica);
        var firstOfMonth = new DateTime(local.Year, local.Month, 1);
        return TimeZoneInfo.ConvertTimeToUtc(firstOfMonth, CostaRica);
    }

    public static bool ShouldArchive(DateTime receivedAtUtc, DateTime nowUtc) =>
        receivedAtUtc < CurrentMonthStartUtc(nowUtc);
}
