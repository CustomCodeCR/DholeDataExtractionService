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

    [TestMethod]
    public async Task ApplyAiResult_WhenEmailUsesMaritimePod_PromotesItToPoeBeforeValidation()
    {
        var pricingImportId = Guid.NewGuid();
        var pipeline = new RecordingPipeline(Success(pricingImportId));
        var service = new AutomatedPricingExtractionService(
            pipeline,
            new ExplodingAiClient(),
            new FakeContentReader(),
            new EmptyConfigCatalogClient(),
            new ConfigurationBuilder().Build(),
            NullLogger<AutomatedPricingExtractionService>.Instance
        );
        var analysis = new AiPricingEmailAnalysisResult(
            true,
            Guid.NewGuid(),
            95m,
            [
                new AiPricingEmailRow(
                    "Shanghai",
                    null,
                    "Caldera",
                    "40HC",
                    "ONE",
                    "WWL",
                    "Auto Spare Parts",
                    "USD",
                    21,
                    null,
                    new DateTime(2026, 8, 8),
                    new DateTime(2026, 8, 14),
                    6400m,
                    null,
                    null,
                    65m,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null
                )
            ],
            []
        );

        var result = await service.ApplyAiResultAsync(
            pricingImportId,
            "email-pod-test",
            "Body",
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            analysis,
            new AutomatedPricingExtractionContext(
                Guid.NewGuid(),
                null,
                "rates@example.com",
                "WWL CONTRACT",
                "POL: Shanghai\nPOD: Caldera",
                null,
                "Body",
                ForceAiAnalysis: false
            )
        );

        Assert.IsTrue(result.Response.Success);
        Assert.HasCount(1, pipeline.Requests);
        var csv = Encoding.UTF8.GetString(pipeline.Requests.Single().FileContent);
        StringAssert.Contains(csv, "Shanghai,Caldera,,40HC,ONE");
        StringAssert.Contains(csv, "POE recuperado desde POD marítimo");
    }

    [TestMethod]
    public async Task PrepareAiRequest_RejectsImageAttachments()
    {
        var pricingImportId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("fake-image-content");
        var service = new AutomatedPricingExtractionService(
            new RecordingPipeline(Success(pricingImportId)),
            new ExplodingAiClient(),
            new FakeContentReader(),
            new EmptyConfigCatalogClient(),
            new ConfigurationBuilder().Build(),
            NullLogger<AutomatedPricingExtractionService>.Instance
        );
        var request = new ExtractionDataRequest(
            pricingImportId,
            "async-email-test",
            "rate.png",
            "image/png",
            ".png",
            content.LongLength,
            "image-hash",
            null,
            null,
            "unit-test",
            content
        )
        {
            SourceOriginType = "EmailAttachment",
            SourceEmailMessageId = Guid.NewGuid(),
            SourceEmailAttachmentId = Guid.NewGuid(),
            StoragePath = "emails/rate.png",
        };

        InvalidOperationException? exception = null;

        try
        {
            await service.PrepareAiRequestAsync(
                request,
                Success(pricingImportId),
                new AutomatedPricingExtractionContext(
                    request.SourceEmailMessageId,
                    request.SourceEmailAttachmentId,
                    "sender@example.com",
                    "Rate",
                    "Short email context",
                    null,
                    "EmailAttachment",
                    ForceAiAnalysis: true
                ),
                request.StoragePath
            );
        }
        catch (InvalidOperationException caught)
        {
            exception = caught;
        }

        Assert.IsNotNull(exception, "Se esperaba InvalidOperationException para adjuntos de imagen.");
        StringAssert.Contains(exception.Message, "XLS, XLSX o XLSM");
    }

    [TestMethod]
    public async Task PrepareAiRequest_AcceptsLegacyExcelAttachment()
    {
        var pricingImportId = Guid.NewGuid();
        var content = Encoding.UTF8.GetBytes("legacy-excel-rate");
        var service = new AutomatedPricingExtractionService(
            new RecordingPipeline(Success(pricingImportId)),
            new ExplodingAiClient(),
            new FakeContentReader(),
            new EmptyConfigCatalogClient(),
            new ConfigurationBuilder().Build(),
            NullLogger<AutomatedPricingExtractionService>.Instance
        );
        var request = new ExtractionDataRequest(
            pricingImportId,
            "legacy-xls-test",
            "YML ASCA FAK TARIFF.xls",
            "application/vnd.ms-excel",
            ".xls",
            content.LongLength,
            "xls-hash",
            null,
            null,
            "unit-test",
            content
        )
        {
            SourceOriginType = "EmailAttachment",
            SourceEmailMessageId = Guid.NewGuid(),
            SourceEmailAttachmentId = Guid.NewGuid(),
            StoragePath = "emails/YML ASCA FAK TARIFF.xls",
        };

        var prepared = await service.PrepareAiRequestAsync(
            request,
            Success(pricingImportId),
            new AutomatedPricingExtractionContext(
                request.SourceEmailMessageId,
                request.SourceEmailAttachmentId,
                "sender@example.com",
                "YML FAK tariff",
                "Tarifa adjunta",
                null,
                "EmailAttachment",
                ForceAiAnalysis: true
            ),
            request.StoragePath
        );

        Assert.IsNotNull(prepared);
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

    private sealed class ExplodingAiClient : IAiExtractionClient
    {
        public Task<AiColumnMappingResult> SuggestColumnMappingsAsync(
            IReadOnlyCollection<string> headers,
            string? rawText,
            string? profileCode = null,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("AI gRPC must not be called.");

        public Task<AiTextNormalizationResult> NormalizePricingTextAsync(
            string rawText,
            string? profileCode = null,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("AI gRPC must not be called.");

        public Task<AiPricingEmailAnalysisResult> AnalyzePricingEmailAsync(
            AiPricingEmailAnalysisRequest request,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("AI gRPC must not be called.");
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

    [TestMethod]
    public async Task ApplyAiResult_NarrativeNacMissingContainer_Defaults40HcAndStoresPodAsPoe()
    {
        var pricingImportId = Guid.NewGuid();
        var pipeline = new RecordingPipeline(Success(pricingImportId));
        var service = new AutomatedPricingExtractionService(
            pipeline,
            new ExplodingAiClient(),
            new FakeContentReader(),
            new EmptyConfigCatalogClient(),
            new ConfigurationBuilder().Build(),
            NullLogger<AutomatedPricingExtractionService>.Instance
        );
        var analysis = new AiPricingEmailAnalysisResult(
            true,
            Guid.NewGuid(),
            95m,
            [
                new AiPricingEmailRow(
                    "Shanghai",
                    null,
                    "Caldera",
                    null,
                    "ONE",
                    null,
                    "Auto Spare Parts",
                    "USD",
                    21,
                    null,
                    new DateTime(2026, 8, 8),
                    new DateTime(2026, 8, 14),
                    6400m,
                    null,
                    null,
                    65m,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null
                ),
            ],
            []
        );
        const string body = """
            Pls consider rate USD6300/6400, valid 8-14/Aug Carrier MSC/ONE NAC with 21 days free at dest
            POL: Shanghai/Kaohsiung/Shekou/Qingdao/Ningbo
            POD: Acajutla/Corinto/Caldera
            COMM: Auto Spare Parts
            """;

        var result = await service.ApplyAiResultAsync(
            pricingImportId,
            "email-nac-container-test",
            "EmailBody",
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            analysis,
            new AutomatedPricingExtractionContext(
                Guid.NewGuid(),
                null,
                "rates@example.com",
                "CASTRO FALLS// WWL CONTRACT ONE-MSC / AUG",
                body,
                null,
                "EmailBody",
                ForceAiAnalysis: false
            )
        );

        Assert.IsTrue(result.Response.Success);
        var csv = Encoding.UTF8.GetString(pipeline.Requests.Single().FileContent);
        StringAssert.Contains(csv, "Shanghai,Caldera,,40HC,ONE");
        StringAssert.Contains(csv, "Equipo 40HC inferido");
    }

    [TestMethod]
    public async Task ApplyAiResult_WwlPairedNac_RebuildsWrongLlamaRowsFromSource()
    {
        var pricingImportId = Guid.NewGuid();
        var pipeline = new RecordingPipeline(Success(pricingImportId));
        var service = new AutomatedPricingExtractionService(
            pipeline,
            new ExplodingAiClient(),
            new FakeContentReader(),
            new EmptyConfigCatalogClient(),
            new ConfigurationBuilder().Build(),
            NullLogger<AutomatedPricingExtractionService>.Instance
        );
        var analysis = new AiPricingEmailAnalysisResult(
            true,
            Guid.NewGuid(),
            100m,
            [
                new AiPricingEmailRow(
                    "Shanghai/Kaohsiung/Shekou/Qingdao/Ningbo",
                    "Acajutla/Corinto/Caldera",
                    null,
                    null,
                    "MSC",
                    "WWL",
                    "Auto Spare Parts",
                    "USD",
                    21,
                    null,
                    new DateTime(2026, 8, 4),
                    new DateTime(2026, 8, 14),
                    6300m,
                    null,
                    null,
                    65m,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null
                ),
                new AiPricingEmailRow(
                    "Shanghai/Ningbo/Shekou/Yantian/Qingdao/Xiamen/Tianjin",
                    "Acajutla/Corinto/Caldera",
                    null,
                    null,
                    "MSC",
                    "WWL",
                    "RETAIL",
                    "USD",
                    21,
                    null,
                    new DateTime(2026, 8, 4),
                    new DateTime(2026, 8, 14),
                    6400m,
                    100m,
                    null,
                    75m,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null
                ),
            ],
            []
        );
        const string body = """
            Pls consider rate USD6300/6400 , valid 8-14/Aug Carrier MSC/ONE NAC with 21 days free at dest, subject to space (except TIANJIN/XIAMEN)
            Subject to isps $15/cntr, p/s $50/cntr, MBL RLS at dest. $75/BL.
            Below the details of ONE NAC:
            Pls note, ONE NAC must match COMM as I listed below
            A)
            POL: Shanghai/Kaohsiung/Shekou/Qingdao/Ningbo
            POD: Acajutla/Corinto/Caldera
            COMM: Auto Spare Parts
            B)
            POL: Shanghai/Ningbo/Shekou/Yantian/Qingdao/Xiamen/Tianjin(+ arb USD100)/Nanjing(+arb USD400)/Wuhan(+arb USD450)/Chongqing(+arb USD850)
            POD: Acajutla/Corinto/Caldera
            COMM: RETAIL
            C)
            POL: Shanghai/Yantian/Qingdao/Ningbo
            POD: Acajutla/Corinto/Caldera
            COMM: Solar Panels/Solar Modules/LED Lights
            """;

        var result = await service.ApplyAiResultAsync(
            pricingImportId,
            "email-wwl-nac-rebuild-test",
            "EmailBody",
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            analysis,
            new AutomatedPricingExtractionContext(
                Guid.NewGuid(),
                null,
                "rates@example.com",
                "CASTRO FALLS | WWL CONTRACT ONE-MSC | AUG",
                body,
                null,
                "EmailBody",
                ForceAiAnalysis: false
            )
        );

        Assert.IsTrue(result.Response.Success);
        var csv = Encoding.UTF8.GetString(pipeline.Requests.Single().FileContent);
        var dataLines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1).ToArray();
        Assert.HasCount(8, dataLines);
        StringAssert.Contains(csv, ",40HC,MSC,WWL,,USD,21,,2026-08-08,2026-08-14,6300,");
        StringAssert.Contains(csv, ",40HC,ONE,WWL,Auto Spare Parts,USD,21,,2026-08-08,2026-08-14,6400,");
        StringAssert.Contains(csv, "Tianjin,Acajutla/Corinto/Caldera,,40HC,ONE,WWL,RETAIL");
        StringAssert.Contains(csv, ",100,,,65,");
        Assert.IsFalse(csv.Contains(",40HC,MSC,WWL,RETAIL", StringComparison.Ordinal));
    }

}
