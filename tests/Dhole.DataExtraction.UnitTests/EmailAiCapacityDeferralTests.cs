using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class EmailAiCapacityDeferralTests
{
    [TestMethod]
    public void DeferralLeavesJobPendingWithoutConsumingAnAttempt()
    {
        var job = EmailExtractionJob.CreateBodyJob(Guid.NewGuid());
        job.MarkExtracting("test-worker", DateTime.UtcNow.AddMinutes(10));
        Assert.AreEqual(1, job.AttemptCount);
        var nextAttempt = DateTime.UtcNow.AddSeconds(90);

        job.DeferForAiCapacity(nextAttempt);

        Assert.AreEqual(EmailExtractionJobStatus.Pending, job.Status);
        Assert.AreEqual(0, job.AttemptCount);
        Assert.AreEqual("DataExtraction.AiCapacityDeferred", job.LastErrorCode);
        Assert.IsNotNull(job.NextAttemptAtUtc);
        Assert.IsNull(job.LeaseOwner);
    }

    [TestMethod]
    public void DeferralRequiresAnActiveExtraction()
    {
        var job = EmailExtractionJob.CreateBodyJob(Guid.NewGuid());
        Assert.ThrowsExactly<InvalidOperationException>(
            () => job.DeferForAiCapacity(DateTime.UtcNow.AddMinutes(1)));
        Assert.AreEqual(EmailExtractionJobStatus.Pending, job.Status);
    }
}
