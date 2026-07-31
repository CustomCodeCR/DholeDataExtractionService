using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Application.Abstractions.Emails;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class AsyncEmailWorkflowTests
{
    [TestMethod]
    public void EmailExtractionJob_TracksAsyncAiAndPricingTransitions()
    {
        var messageId = Guid.NewGuid();
        var attachmentId = Guid.NewGuid();
        var extractionId = Guid.NewGuid();
        var aiRequestId = Guid.NewGuid();
        var aiExecutionId = Guid.NewGuid();
        var pricingRequestId = Guid.NewGuid();
        var pricingBatchId = Guid.NewGuid();
        var job = EmailExtractionJob.CreateAttachmentJob(
            messageId,
            attachmentId
        );

        job.MarkExtracting("unit-test-worker", DateTime.UtcNow.AddMinutes(5));
        job.MarkAwaitingAi(aiRequestId, extractionId, "request-hash");
        job.MarkAiProcessing(aiRequestId);
        job.MarkValidatingAiResult(aiRequestId, aiExecutionId);
        job.MarkAwaitingPricing(pricingRequestId, extractionId, 96m);
        job.MarkSentToPricing(extractionId, pricingBatchId, 96m);

        Assert.AreEqual(EmailExtractionJobStatus.SentToPricing, job.Status);
        Assert.AreEqual(aiRequestId, job.AiRequestId);
        Assert.AreEqual(aiExecutionId, job.AiExecutionId);
        Assert.AreEqual(pricingRequestId, job.PricingRequestId);
        Assert.AreEqual(pricingBatchId, job.PricingImportBatchId);
        Assert.AreEqual(1, job.AttemptCount);
        Assert.IsNull(job.LeaseOwner);
        Assert.IsNotNull(job.FinishedAt);
    }

    [TestMethod]
    public void EmailExtractionJob_IgnoresLateAiStartedEvent()
    {
        var job = EmailExtractionJob.CreateBodyJob(Guid.NewGuid());
        var aiRequestId = Guid.NewGuid();
        var extractionId = Guid.NewGuid();

        job.MarkExtracting("unit-test-worker", DateTime.UtcNow.AddMinutes(5));
        job.MarkAwaitingAi(aiRequestId, extractionId, "request-hash");
        job.MarkValidatingAiResult(aiRequestId, Guid.NewGuid());
        job.MarkAwaitingPricing(Guid.NewGuid(), extractionId, 95m);

        job.MarkAiProcessing(aiRequestId);

        Assert.AreEqual(EmailExtractionJobStatus.AwaitingPricing, job.Status);
    }

    [TestMethod]
    public void EmailExtractionJob_RejectsEventForDifferentAiRequest()
    {
        var job = EmailExtractionJob.CreateBodyJob(Guid.NewGuid());
        var activeRequestId = Guid.NewGuid();

        job.MarkExtracting("unit-test-worker", DateTime.UtcNow.AddMinutes(5));
        job.MarkAwaitingAi(
            activeRequestId,
            Guid.NewGuid(),
            "request-hash"
        );

        Assert.ThrowsExactly<InvalidOperationException>(
            () => job.MarkAiProcessing(Guid.NewGuid())
        );
        Assert.AreEqual(EmailExtractionJobStatus.AwaitingAi, job.Status);
    }

    [TestMethod]
    public void LocalLeaseRetry_DoesNotRecoverAwaitingAiJob()
    {
        var job = EmailExtractionJob.CreateBodyJob(Guid.NewGuid());
        var requestId = Guid.NewGuid();

        job.MarkExtracting(
            "unit-test-worker",
            DateTime.UtcNow.AddMilliseconds(1)
        );
        job.MarkAwaitingAi(requestId, Guid.NewGuid(), "request-hash");

        Assert.ThrowsExactly<InvalidOperationException>(
            () =>
                job.ScheduleRetry(
                    "DataExtraction.LeaseExpired",
                    "expired",
                    DateTime.UtcNow
                )
        );
        Assert.AreEqual(EmailExtractionJobStatus.AwaitingAi, job.Status);
    }

    [TestMethod]
    public void EmailExtractionWorker_HasNoBlockingAiOrPricingClientDependency()
    {
        var workerType = typeof(Dhole.DataExtraction.Workers.Worker)
            .Assembly.GetType(
                "Dhole.DataExtraction.Workers.Workers.EmailExtractionWorker",
                throwOnError: true
            )!;
        var dependencyTypes = workerType
            .GetConstructors(
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
            )
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        CollectionAssert.DoesNotContain(
            dependencyTypes,
            typeof(IAiExtractionClient)
        );
        CollectionAssert.DoesNotContain(
            dependencyTypes,
            typeof(IPricingImportClient)
        );
        CollectionAssert.Contains(
            dependencyTypes,
            typeof(IAutomatedPricingExtractionService)
        );
    }
}
