using Dhole.DataExtraction.Domain.Emails;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using Dhole.DataExtraction.Infrastructure.GrpcClients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class DocumentOcrIntegrationTests
{
    [TestMethod]
    public void EmailAttachmentPolicy_AcceptsTariffImagesAndRejectsUnsupportedFormats()
    {
        Assert.IsTrue(EmailAttachmentExtractionPolicy.IsSupported(SourceFileType.Image, ".png"));
        Assert.IsTrue(EmailAttachmentExtractionPolicy.IsSupported(SourceFileType.Image, ".jpeg"));
        Assert.IsTrue(EmailAttachmentExtractionPolicy.IsSupported(SourceFileType.Image, ".webp"));
        Assert.IsTrue(EmailAttachmentExtractionPolicy.IsSupported(SourceFileType.Image, ".tiff"));
        Assert.IsFalse(EmailAttachmentExtractionPolicy.IsSupported(SourceFileType.Image, ".gif"));
        Assert.IsFalse(EmailAttachmentExtractionPolicy.IsSupported(SourceFileType.Pdf, ".png"));
    }

    [TestMethod]
    public async Task AiContentReader_UsesSharedOcrForImageAttachments()
    {
        var configuration = new ConfigurationBuilder().Build();
        var ocr = new StubOcrService();
        var reader = new AiEmailContentReader(
            configuration,
            NullLogger<AiEmailContentReader>.Instance,
            ocr
        );

        var content = await reader.ReadAsTextAsync(
            "tarifa-compania.png",
            "image/png",
            ".png",
            [1, 2, 3]
        );

        Assert.IsTrue(content.Contains("POL Shanghai", StringComparison.Ordinal));
        Assert.IsTrue(content.Contains("USD 1200", StringComparison.Ordinal));
        Assert.AreEqual(1, ocr.ImageCalls);
    }

    [TestMethod]
    public async Task OcrEngine_Disabled_DoesNotStartExternalProcesses()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AI:DocumentOcr:Enabled"] = "false"
            })
            .Build();
        var engine = new DocumentOcrService(
            configuration,
            NullLogger<DocumentOcrService>.Instance
        );

        var image = await engine.RecognizeImageAsync([1, 2, 3], ".png");
        var page = await engine.RecognizePdfPageAsync([1, 2, 3], 1);
        Assert.AreEqual(string.Empty, image);
        Assert.AreEqual(string.Empty, page);
    }

    private sealed class StubOcrService : IDocumentOcrService
    {
        public int ImageCalls { get; private set; }

        public Task<string> RecognizePdfPageAsync(
            byte[] pdfContent, int pageNumber, CancellationToken cancellationToken = default
        ) => Task.FromResult("Shanghai Moin USD 1200");

        public Task<string> RecognizeImageAsync(
            byte[] imageContent, string extension, CancellationToken cancellationToken = default
        )
        {
            ImageCalls++;
            return Task.FromResult("POL Shanghai / POE Moin / USD 1200");
        }
    }
}
