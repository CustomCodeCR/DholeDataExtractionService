using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Contracts.Extraction;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Infrastructure.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhole.DataExtraction.IntegrationTests;

[TestClass]
public sealed class DurableEmailWorkflowIntegrationTests
{
    [TestMethod]
    public async Task FakeAiAndPricingResults_AdvanceJobFromEmailToSentToPricing()
    {
        var messageId = Guid.NewGuid();
        var aiRequestId = Guid.NewGuid();
        var aiExecutionId = Guid.NewGuid();
        var finalExtractionId = Guid.NewGuid();
        var job = EmailExtractionJob.CreateBodyJob(messageId);
        var pipeline = new SuccessfulAiValidationPipeline(
            job.ProvisionalPricingImportId,
            finalExtractionId
        );
        var service = new AutomatedPricingExtractionService(
            pipeline,
            new NeverCalledAiClient(),
            new NeverCalledContentReader(),
            new NeverCalledCatalogClient(),
            new ConfigurationBuilder().Build(),
            NullLogger<AutomatedPricingExtractionService>.Instance
        );

        job.MarkExtracting("data-extraction", DateTime.UtcNow.AddMinutes(5));
        job.MarkAwaitingAi(
            aiRequestId,
            Guid.NewGuid(),
            "request-hash"
        );
        job.MarkAiProcessing(aiRequestId);
        job.MarkValidatingAiResult(aiRequestId, aiExecutionId);

        var validated = await service.ApplyAiResultAsync(
            job.ProvisionalPricingImportId,
            "email-correlation",
            "Body",
            messageId,
            messageId,
            null,
            new AiPricingEmailAnalysisResult(
                true,
                aiExecutionId,
                97m,
                [CreateAiRow()],
                []
            )
        );

        Assert.IsTrue(validated.Response.Success);
        Assert.AreEqual(1, pipeline.CallCount);
        Assert.AreEqual(
            "BodyAiFallback",
            pipeline.LastRequest?.SourceOriginType
        );

        var pricingRequestId = Guid.NewGuid();
        job.MarkAwaitingPricing(
            pricingRequestId,
            finalExtractionId,
            97m
        );
        job.MarkSentToPricing(
            finalExtractionId,
            job.ProvisionalPricingImportId,
            97m
        );

        Assert.AreEqual(EmailExtractionJobStatus.SentToPricing, job.Status);
        Assert.AreEqual(aiExecutionId, job.AiExecutionId);
        Assert.AreEqual(pricingRequestId, job.PricingRequestId);
        Assert.AreEqual("email-correlation", pipeline.LastRequest?.CorrelationId);
    }

    [TestMethod]
    public void AwaitingAiJob_DoesNotBlockAnotherEmailJob()
    {
        var first = EmailExtractionJob.CreateBodyJob(Guid.NewGuid());
        var second = EmailExtractionJob.CreateBodyJob(Guid.NewGuid());

        first.MarkExtracting("worker-a", DateTime.UtcNow.AddMinutes(5));
        first.MarkAwaitingAi(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "request-hash"
        );

        Assert.AreEqual(EmailExtractionJobStatus.AwaitingAi, first.Status);
        Assert.AreEqual(EmailExtractionJobStatus.Pending, second.Status);
        second.MarkExtracting("worker-b", DateTime.UtcNow.AddMinutes(5));
        Assert.AreEqual(EmailExtractionJobStatus.Extracting, second.Status);
    }

    private static AiPricingEmailRow CreateAiRow()
    {
        return new AiPricingEmailRow(
            "Shanghai",
            "Moín",
            null,
            "40HC",
            "MSC",
            null,
            null,
            "USD",
            14,
            25,
            new DateTime(2026, 8, 1),
            new DateTime(2026, 8, 31),
            1250m,
            null,
            null,
            null,
            1250m,
            1500m,
            250m,
            16.67m,
            null,
            null
        );
    }

    private sealed class SuccessfulAiValidationPipeline(
        Guid pricingImportId,
        Guid extractionId
    ) : IExtractionPipeline
    {
        public int CallCount { get; private set; }

        public ExtractionDataRequest? LastRequest { get; private set; }

        public Task<ExtractPricingDataResponse> ExtractPricingDataAsync(
            ExtractionDataRequest request,
            CancellationToken cancellationToken = default
        )
        {
            CallCount++;
            LastRequest = request;
            return Task.FromResult(
                new ExtractPricingDataResponse(
                    true,
                    extractionId,
                    pricingImportId,
                    request.CorrelationId,
                    new ExtractionSummaryDto(1, 1, 0, 0, false),
                    null,
                    [
                        new ExtractedPricingRowDto(
                            Guid.NewGuid(),
                            extractionId,
                            Guid.NewGuid(),
                            "AI",
                            2,
                            "Shanghai",
                            "Moín",
                            null,
                            "40HC",
                            "MSC",
                            null,
                            null,
                            "USD",
                            14,
                            25,
                            new DateTime(2026, 8, 1),
                            new DateTime(2026, 8, 31),
                            1250m,
                            null,
                            null,
                            null,
                            1250m,
                            1500m,
                            250m,
                            16.67m,
                            null,
                            null,
                            "Valid",
                            "{}"
                        ),
                    ],
                    [],
                    null,
                    null
                )
            );
        }
    }

    private sealed class NeverCalledAiClient : IAiExtractionClient
    {
        public Task<AiColumnMappingResult> SuggestColumnMappingsAsync(
            IReadOnlyCollection<string> headers,
            string? rawText,
            string? profileCode = null,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("Not expected.");

        public Task<AiTextNormalizationResult> NormalizePricingTextAsync(
            string rawText,
            string? profileCode = null,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("Not expected.");

        public Task<AiPricingEmailAnalysisResult> AnalyzePricingEmailAsync(
            AiPricingEmailAnalysisRequest request,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("Not expected.");
    }

    private sealed class NeverCalledContentReader : IAiEmailContentReader
    {
        public Task<string> ReadAsTextAsync(
            string fileName,
            string? contentType,
            string? fileExtension,
            byte[] content,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("Not expected.");
    }

    private sealed class NeverCalledCatalogClient : IConfigCatalogClient
    {
        public Task<ConfigCatalogItemResult?> ResolveCatalogItemAsync(
            string catalogGroupSlug,
            string value,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("Not expected.");

        public Task<bool> ValidateCatalogItemAsync(
            string catalogGroupSlug,
            string catalogItemSlug,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("Not expected.");

        public Task<
            IReadOnlyCollection<ConfigCatalogItemResult>
        > GetActiveCatalogItemsByGroupAsync(
            string catalogGroupSlug,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("Not expected.");
    }
}
