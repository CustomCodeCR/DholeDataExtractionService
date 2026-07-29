using System.Text;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Contracts.Extraction;
using Dhole.DataExtraction.Infrastructure.Pipeline;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class AutomatedPricingExtractionServiceTests
{
    [TestMethod]
    public async Task ManualUpload_UsesAiAndRevalidatesCombinedContainersWithOriginalProfile()
    {
        var pricingImportId = Guid.NewGuid();
        var pipeline = new RecordingPipeline(
            Failure(pricingImportId),
            Success(pricingImportId)
        );
        var aiClient = new FakeAiClient(
            new AiPricingEmailRow(
                "Shanghai",
                "Moín",
                "San José",
                "40'/40HC",
                "MSC",
                null,
                null,
                "USD",
                14,
                null,
                new DateTime(2026, 8, 1),
                new DateTime(2026, 8, 31),
                6210m,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null
            )
        );
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AI:AutomaticExtraction:Enabled"] = "true",
                    ["AI:AutomaticExtraction:AnalyzeEverySource"] = "true",
                }
            )
            .Build();
        var service = new AutomatedPricingExtractionService(
            pipeline,
            aiClient,
            new FakeContentReader(),
            new EmptyConfigCatalogClient(),
            configuration,
            NullLogger<AutomatedPricingExtractionService>.Instance
        );
        var content = Encoding.UTF8.GetBytes("tarifa marítima adjunta");
        var request = new ExtractionDataRequest(
            pricingImportId,
            "manual-test",
            "tarifa.pdf",
            "application/pdf",
            ".pdf",
            content.LongLength,
            "hash",
            "fcl-default",
            Guid.NewGuid(),
            "Maurice",
            content
        )
        {
            SourceOriginType = "ManualUpload",
        };

        var result = await service.ExtractAsync(request);

        Assert.IsTrue(result.AiAttempted);
        Assert.IsTrue(result.AiApplied);
        Assert.HasCount(2, pipeline.Requests);

        var normalizedRequest = pipeline.Requests[1];
        Assert.AreEqual("fcl-default", normalizedRequest.ProfileCode);
        Assert.AreEqual("ManualUploadAiFallback", normalizedRequest.SourceOriginType);

        var csv = Encoding.UTF8.GetString(normalizedRequest.FileContent);
        var lines = csv
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .ToArray();
        Assert.HasCount(3, lines);
        Assert.Contains("Shanghai,Moín,San José,40DV,MSC", lines[1]);
        Assert.Contains("Shanghai,Moín,San José,40HC,MSC", lines[2]);
    }

    [TestMethod]
    public async Task ManualUpload_WhenAiFails_DoesNotAllowDeterministicResultToReachPricing()
    {
        var pricingImportId = Guid.NewGuid();
        var pipeline = new RecordingPipeline(Success(pricingImportId));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["AI:AutomaticExtraction:Enabled"] = "true",
                    ["AI:AutomaticExtraction:AnalyzeEverySource"] = "true",
                    ["AI:AutomaticExtraction:RequireAiResult"] = "true",
                }
            )
            .Build();
        var service = new AutomatedPricingExtractionService(
            pipeline,
            new FailingAiClient(),
            new FakeContentReader(),
            new EmptyConfigCatalogClient(),
            configuration,
            NullLogger<AutomatedPricingExtractionService>.Instance
        );
        var content = Encoding.UTF8.GetBytes("tarifa marítima adjunta");
        var request = new ExtractionDataRequest(
            pricingImportId,
            "manual-ai-required-test",
            "tarifa.csv",
            "text/csv",
            ".csv",
            content.LongLength,
            "hash",
            "fcl-default",
            Guid.NewGuid(),
            "Maurice",
            content
        );

        var result = await service.ExtractAsync(request);

        Assert.IsTrue(result.AiAttempted);
        Assert.IsFalse(result.AiApplied);
        Assert.IsFalse(result.Response.Success);
        Assert.AreEqual("AI.RequiredFormattingFailed", result.Response.ErrorCode);
        Assert.HasCount(1, pipeline.Requests);
    }

    private static ExtractPricingDataResponse Failure(Guid pricingImportId)
    {
        return new ExtractPricingDataResponse(
            false,
            Guid.NewGuid(),
            pricingImportId,
            "manual-test",
            new ExtractionSummaryDto(0, 0, 0, 0, true),
            null,
            [],
            [],
            "DataExtraction.ExtractionFailed",
            "No se reconoció el formato."
        );
    }

    private static ExtractPricingDataResponse Success(Guid pricingImportId)
    {
        var executionId = Guid.NewGuid();
        var sourceDocumentId = Guid.NewGuid();
        var rows = new[]
        {
            Row(executionId, sourceDocumentId, "40DV"),
            Row(executionId, sourceDocumentId, "40HC"),
        };

        return new ExtractPricingDataResponse(
            true,
            executionId,
            pricingImportId,
            "manual-test",
            new ExtractionSummaryDto(2, 2, 0, 0, false),
            null,
            rows,
            [],
            null,
            null
        );
    }

    private static ExtractedPricingRowDto Row(
        Guid executionId,
        Guid sourceDocumentId,
        string containerType
    )
    {
        return new ExtractedPricingRowDto(
            Guid.NewGuid(),
            executionId,
            sourceDocumentId,
            "AI",
            2,
            "Shanghai",
            "Moín",
            null,
            containerType,
            "MSC",
            null,
            null,
            "USD",
            14,
            null,
            new DateTime(2026, 8, 1),
            new DateTime(2026, 8, 31),
            6210m,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            "Valid",
            "{}"
        );
    }

    private sealed class RecordingPipeline(
        params ExtractPricingDataResponse[] responses
    ) : IExtractionPipeline
    {
        private readonly Queue<ExtractPricingDataResponse> _responses = new(responses);

        public List<ExtractionDataRequest> Requests { get; } = [];

        public Task<ExtractPricingDataResponse> ExtractPricingDataAsync(
            ExtractionDataRequest request,
            CancellationToken cancellationToken = default
        )
        {
            Requests.Add(request);
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class FakeContentReader : IAiEmailContentReader
    {
        public Task<string> ReadAsTextAsync(
            string fileName,
            string? contentType,
            string? fileExtension,
            byte[] content,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(Encoding.UTF8.GetString(content));
    }

    private sealed class FakeAiClient(AiPricingEmailRow row) : IAiExtractionClient
    {
        public Task<AiColumnMappingResult> SuggestColumnMappingsAsync(
            IReadOnlyCollection<string> headers,
            string? rawText,
            string? profileCode = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new AiColumnMappingResult(true, []));

        public Task<AiTextNormalizationResult> NormalizePricingTextAsync(
            string rawText,
            string? profileCode = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new AiTextNormalizationResult(true, rawText));

        public Task<AiPricingEmailAnalysisResult> AnalyzePricingEmailAsync(
            AiPricingEmailAnalysisRequest request,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(
            new AiPricingEmailAnalysisResult(
                true,
                Guid.NewGuid(),
                95m,
                [row],
                []
            )
        );
    }
    private sealed class FailingAiClient : IAiExtractionClient
    {
        public Task<AiColumnMappingResult> SuggestColumnMappingsAsync(
            IReadOnlyCollection<string> headers,
            string? rawText,
            string? profileCode = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new AiColumnMappingResult(false, [], "AI unavailable"));

        public Task<AiTextNormalizationResult> NormalizePricingTextAsync(
            string rawText,
            string? profileCode = null,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new AiTextNormalizationResult(false, null, "AI unavailable"));

        public Task<AiPricingEmailAnalysisResult> AnalyzePricingEmailAsync(
            AiPricingEmailAnalysisRequest request,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(
            new AiPricingEmailAnalysisResult(
                false,
                null,
                0m,
                [],
                [],
                "AI.Unavailable",
                "AI unavailable"
            )
        );
    }

    private sealed class EmptyConfigCatalogClient : IConfigCatalogClient
    {
        public Task<ConfigCatalogItemResult?> ResolveCatalogItemAsync(
            string catalogGroupSlug,
            string value,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<ConfigCatalogItemResult?>(null);

        public Task<bool> ValidateCatalogItemAsync(
            string catalogGroupSlug,
            string catalogItemSlug,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(false);

        public Task<IReadOnlyCollection<ConfigCatalogItemResult>> GetActiveCatalogItemsByGroupAsync(
            string catalogGroupSlug,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<IReadOnlyCollection<ConfigCatalogItemResult>>([]);
    }

}
