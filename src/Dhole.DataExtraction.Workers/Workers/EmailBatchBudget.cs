namespace Dhole.DataExtraction.Workers.Workers;

/// <summary>
/// Computes a fixed, bounded dispatch budget without oversubscribing workers.
/// </summary>
public static class EmailBatchBudget
{
    public static int[] Distribute(int configuredJobs, int configuredParallelism)
    {
        var totalJobs = Math.Clamp(configuredJobs, 1, 500);
        var slots = Math.Min(Math.Clamp(configuredParallelism, 1, 8), totalJobs);
        return Enumerable.Range(0, slots)
            .Select(slot => totalJobs / slots + (slot < totalJobs % slots ? 1 : 0))
            .ToArray();
    }
}
