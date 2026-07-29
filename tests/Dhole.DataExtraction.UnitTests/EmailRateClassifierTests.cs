using Dhole.DataExtraction.Contracts.Extraction;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using Dhole.DataExtraction.Infrastructure.Email;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class EmailRateClassifierTests
{
    [TestMethod]
    public void PlainTextPricingBody_WithAttachment_IsQueuedWithoutRequiringTableMarkup()
    {
        var account = EmailIngestionAccount.Create(
            "Tarifas",
            "rates@example.com",
            EmailProviderType.Gmail,
            null,
            993,
            true,
            "rates@example.com",
            "DATA_EXTRACTION_EMAIL_PASSWORD",
            "INBOX",
            5,
            true,
            true,
            90m,
            true,
            true,
            "*",
            null
        );
        var message = EmailMessage.Create(
            account.Id,
            "message-1",
            1,
            null,
            "Andrea",
            "andrea@example.com",
            "rates@example.com",
            null,
            "Tarifas marítimas FCL agosto",
            """
            POL POD Naviera 20' 40'/40HC Días Libres Inicio Vence
            Shanghai Moín MSC $5,980 $6,210 14 días 01-Aug-2026 31-Aug-2026
            """,
            null,
            DateTime.UtcNow,
            true,
            null,
            null
        );
        var attachment = EmailAttachment.Create(
            message.Id,
            "tarifas.pdf",
            "application/pdf",
            ".pdf",
            100,
            "hash",
            "storage/tarifas.pdf",
            SourceFileType.Pdf
        );

        var result = new EmailRateClassifier().Classify(
            message,
            [attachment],
            account
        );

        Assert.IsTrue(result.ContainsRates);
        Assert.IsTrue(result.ProcessBody);
        CollectionAssert.Contains(result.AttachmentIdsToProcess.ToArray(), attachment.Id);
    }

    [TestMethod]
    public void AttachmentOnlyMessage_DoesNotCreateEmptyBodyExtractionJob()
    {
        var account = EmailIngestionAccount.Create(
            "Tarifas",
            "rates@example.com",
            EmailProviderType.Gmail,
            null,
            993,
            true,
            "rates@example.com",
            "DATA_EXTRACTION_EMAIL_PASSWORD",
            "INBOX",
            5,
            true,
            true,
            90m,
            true,
            true,
            "*",
            null
        );
        var message = EmailMessage.Create(
            account.Id,
            "message-2",
            2,
            null,
            "Andrea",
            "andrea@example.com",
            "rates@example.com",
            null,
            "Tarifa actualizada",
            "Estimados, adjunto encontrarán la tarifa actualizada. Saludos.",
            null,
            DateTime.UtcNow,
            true,
            null,
            null
        );
        var attachment = EmailAttachment.Create(
            message.Id,
            "tarifas.pdf",
            "application/pdf",
            ".pdf",
            100,
            "hash-2",
            "storage/tarifas-2.pdf",
            SourceFileType.Pdf
        );

        var result = new EmailRateClassifier().Classify(
            message,
            [attachment],
            account
        );

        Assert.IsTrue(result.ContainsRates);
        Assert.IsFalse(result.ProcessBody);
        CollectionAssert.Contains(result.AttachmentIdsToProcess.ToArray(), attachment.Id);
    }

    [TestMethod]
    public void OptionalAgentAndExpiredRateIssues_KeepBodyAtReviewThreshold()
    {
        var rowId = Guid.NewGuid();
        var response = CreateResponse(
            [
                CreateIssue(rowId, "missing_agent", false),
                CreateIssue(rowId, "expired_rate", false),
            ]
        );

        var confidence = new EmailRateClassifier().CalculateExtractionConfidence(
            response,
            null!,
            null
        );

        Assert.AreEqual(90m, confidence);
    }

    [TestMethod]
    public void UnknownConfigValue_RemainsReviewableWithoutZeroingConfidence()
    {
        var rowId = Guid.NewGuid();
        var response = CreateResponse([CreateIssue(rowId, "unknown_carrier", true)]);

        var confidence = new EmailRateClassifier().CalculateExtractionConfidence(
            response,
            null!,
            null
        );

        Assert.AreEqual(90m, confidence);
    }

    [TestMethod]
    public void StructuralBlockingIssue_PreventsAutomaticSend()
    {
        var rowId = Guid.NewGuid();
        var response = CreateResponse([CreateIssue(rowId, "missing_origin_port", true)]);

        var confidence = new EmailRateClassifier().CalculateExtractionConfidence(
            response,
            null!,
            null
        );

        Assert.AreEqual(0m, confidence);
    }

    private static ExtractPricingDataResponse CreateResponse(
        IReadOnlyCollection<ExtractionIssueDto> issues
    )
    {
        return new ExtractPricingDataResponse(
            true,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid().ToString(),
            new ExtractionSummaryDto(1, 0, 0, 1, true),
            null,
            [],
            issues,
            null,
            null,
            null
        );
    }

    private static ExtractionIssueDto CreateIssue(Guid rowId, string code, bool isBlocking)
    {
        return new ExtractionIssueDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            rowId,
            code,
            code,
            isBlocking,
            "Rates",
            2,
            null,
            null
        );
    }
}
