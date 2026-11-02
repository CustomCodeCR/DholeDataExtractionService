using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class EmailAutomaticReprocessTests
{
    [TestMethod]
    public void AutomaticRetry_PreservesSourceKeyAndAttemptCount()
    {
        var job = EmailExtractionJob.CreateBodyJob(Guid.NewGuid());
        var originalImportId = job.ProvisionalPricingImportId;
        job.MarkExtracting("test-worker", DateTime.UtcNow.AddMinutes(5));
        job.MarkNeedsReview(Guid.NewGuid(), 60m, "missing_port_of_exit",
            "missing_port_of_exit");

        var previousAttempts = job.AttemptCount;
        var nextRun = DateTime.UtcNow.AddHours(1);
        job.ScheduleAutomaticReprocess(nextRun);

        Assert.AreEqual(EmailExtractionJobStatus.Pending, job.Status);
        Assert.AreEqual(previousAttempts, job.AttemptCount);
        Assert.AreEqual(originalImportId, job.ProvisionalPricingImportId);
        Assert.AreEqual(nextRun, job.NextAttemptAtUtc);
        Assert.IsNull(job.PricingImportBatchId);
        Assert.IsNull(job.PricingRequestId);
    }

    [TestMethod]
    public void AutomaticRetry_RefusesSuccessfullyImportedSource()
    {
        var job = EmailExtractionJob.CreateBodyJob(Guid.NewGuid());
        job.MarkExtracting("test-worker", DateTime.UtcNow.AddMinutes(5));
        job.MarkSentToPricing(Guid.NewGuid(), Guid.NewGuid(), 100m);

        Assert.Throws<InvalidOperationException>(() =>
            job.ScheduleAutomaticReprocess(DateTime.UtcNow));
    }

    [TestMethod]
    public void AutomaticRetry_DoesNotInterruptActiveExtraction()
    {
        var job = EmailExtractionJob.CreateBodyJob(Guid.NewGuid());
        job.MarkExtracting("test-worker", DateTime.UtcNow.AddMinutes(5));

        Assert.Throws<InvalidOperationException>(() =>
            job.ScheduleAutomaticReprocess(DateTime.UtcNow));
    }
}
