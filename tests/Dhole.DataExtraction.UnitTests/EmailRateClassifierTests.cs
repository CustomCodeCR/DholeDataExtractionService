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
    public void NarrativeNorRate_WithTrailingCurrencySymbolAndValidez_IsQueued()
    {
        var account = CreateAccount();
        var message = EmailMessage.Create(
            account.Id,
            "message-msk-40nor",
            27,
            null,
            "Pricing",
            "pricing@example.com",
            "rates@example.com",
            null,
            "Tarifa MSK Shanghai Balboa",
            """
            Carrier MSK
            Flete internacional 7600$
            1x40NOR
            POL: SHANGHAI
            POE: BALBOA PANAMA
            ETD 6 SETIEMBRE FECHA VALIDEZ 2026

            Website: https://logisticacastrofallas.com
            AVISO LEGAL: Este mensaje es confidencial.
            """,
            null,
            DateTime.UtcNow,
            true,
            null,
            null
        );

        var result = new EmailRateClassifier().Classify(message, [], account);

        Assert.IsTrue(result.ContainsRates, result.Reason);
        Assert.IsTrue(result.ProcessBody, result.Reason);
    }

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
    public void AttachmentWithQuotedHistoricalRequest_DoesNotCreateEmptyBodyImport()
    {
        var account = CreateAccount();
        var message = EmailMessage.Create(
            account.Id,
            "message-agunsa-pdf",
            3,
            null,
            "Jonathan",
            "pricing.cr@agunsa.com",
            "rates@example.com",
            null,
            "RE: Tarifarios Agosto 2026",
            """
            Buenos días

            Adjunto tarifas requeridas

            Jonathan Guerrero
            Pricing Costa Rica and Nicaragua
            Phone: +506 7060 1324

            ________________________________________________
            De: Marco Artavia <pricing@castrofallas.com>
            Enviado: martes, 4 de agosto de 2026 20:37
            Asunto: RE: Tarifarios Agosto 2026

            Agradecemos nos puedan compartir las tarifas del 08 al 15 de agosto.
            Requerimos POD Caldera y Moín para contenedores 20' y 40HC.
            """,
            null,
            DateTime.UtcNow,
            true,
            null,
            null
        );
        var attachment = EmailAttachment.Create(
            message.Id,
            "tarifario-pil.pdf",
            "application/pdf",
            ".pdf",
            100,
            "hash-agunsa-pdf",
            "storage/tarifario-pil.pdf",
            SourceFileType.Pdf
        );

        var result = new EmailRateClassifier().Classify(message, [attachment], account);

        Assert.IsTrue(result.ContainsRates);
        Assert.IsFalse(result.ProcessBody);
        CollectionAssert.Contains(result.AttachmentIdsToProcess.ToArray(), attachment.Id);
    }

    [TestMethod]
    public void AttachmentWithQuotedHistoricalRates_DoesNotCreateDuplicateBodyImport()
    {
        var account = CreateAccount();
        var message = EmailMessage.Create(
            account.Id,
            "message-quoted-rates",
            4,
            null,
            "Jonathan",
            "pricing.cr@agunsa.com",
            "rates@example.com",
            null,
            "RE: Tarifarios Agosto 2026",
            """
            Buenos días

            Adjunto tarifas requeridas.

            Jonathan Guerrero
            Pricing Costa Rica and Nicaragua

            ________________________________________________
            De: Proveedor anterior <rates@example.com>
            Enviado: martes, 28 de julio de 2026 09:31
            Asunto: Tarifas anteriores

            POL POD CARRIER 20' 40HC Effective Date Expiry date
            Shanghai Caldera PIL $6,560 $6,830 8-Aug 14-Aug
            """,
            null,
            DateTime.UtcNow,
            true,
            null,
            null
        );
        var attachment = EmailAttachment.Create(
            message.Id,
            "tarifario-actual.pdf",
            "application/pdf",
            ".pdf",
            100,
            "hash-current-pdf",
            "storage/tarifario-actual.pdf",
            SourceFileType.Pdf
        );

        var result = new EmailRateClassifier().Classify(message, [attachment], account);

        Assert.IsTrue(result.ContainsRates);
        Assert.IsFalse(result.ProcessBody);
        CollectionAssert.Contains(result.AttachmentIdsToProcess.ToArray(), attachment.Id);
    }

    [TestMethod]
    public void ImageAttachment_IsStoredButNotQueuedForExtraction()
    {
        var account = CreateAccount();
        var message = CreateAttachmentOnlyMessage(account.Id, "message-image");
        var attachment = EmailAttachment.Create(
            message.Id,
            "rate.png",
            "image/png",
            ".png",
            100,
            "hash-image",
            "storage/rate.png",
            SourceFileType.Image
        );

        var result = new EmailRateClassifier().Classify(message, [attachment], account);

        Assert.IsFalse(result.ContainsRates);
        Assert.IsFalse(result.ProcessBody);
        Assert.HasCount(0, result.AttachmentIdsToProcess);
    }

    [TestMethod]
    public void XlsxAttachment_IsQueuedButLegacyXlsIsNot()
    {
        var account = CreateAccount();
        var message = CreateAttachmentOnlyMessage(account.Id, "message-excel");
        var xlsx = EmailAttachment.Create(
            message.Id,
            "rate.xlsx",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xlsx",
            100,
            "hash-xlsx",
            "storage/rate.xlsx",
            SourceFileType.Excel
        );
        var xls = EmailAttachment.Create(
            message.Id,
            "legacy.xls",
            "application/vnd.ms-excel",
            ".xls",
            100,
            "hash-xls",
            "storage/legacy.xls",
            SourceFileType.Excel
        );

        var result = new EmailRateClassifier().Classify(message, [xlsx, xls], account);

        Assert.IsTrue(result.ContainsRates);
        CollectionAssert.AreEqual(
            new[] { xlsx.Id },
            result.AttachmentIdsToProcess.ToArray()
        );
    }

    [TestMethod]
    public void StructuredBody_WithSeparateCurrencyAndFreightAmount_IsProcessed()
    {
        var account = CreateAccount();
        var message = EmailMessage.Create(
            account.Id,
            "message-structured-amount",
            10,
            null,
            "Pricing",
            "pricing@example.com",
            "rates@example.com",
            null,
            "Tarifa marítima estructurada",
            """
            Carrier: MAERSK
            POL: Shanghai
            POE: Manzanillo
            ContainerSize: 40HC
            Currency: USD
            FreightAmount: 2450.00
            ValidFrom: 2026-08-01
            ValidTo: 2026-08-31
            """,
            null,
            DateTime.UtcNow,
            true,
            null,
            null
        );

        var result = new EmailRateClassifier().Classify(message, [], account);

        Assert.IsTrue(result.ContainsRates);
        Assert.IsTrue(result.ProcessBody);
    }

    [TestMethod]
    public void NarrativeNacBody_IsClassifiedAboveAiBypassThreshold()
    {
        var account = CreateAccount();
        var message = EmailMessage.Create(
            account.Id,
            "message-nac",
            11,
            null,
            "Veronica",
            "veronica@example.com",
            "rates@example.com",
            null,
            "CASTRO FALLS// WWL CONTRACT ONE-MSC / AUG",
            """
            Pls consider rate USD6300/6400, valid 8-14/Aug Carrier MSC/ONE NAC with 21 days free at dest.
            POL: Shanghai/Ningbo/Qingdao
            POD: Acajutla/Corinto/Caldera
            COMM: Auto Spare Parts
            """,
            null,
            DateTime.UtcNow,
            true,
            null,
            null
        );

        var result = new EmailRateClassifier().Classify(message, [], account);

        Assert.IsTrue(result.ContainsRates);
        Assert.IsTrue(result.ProcessBody);
        Assert.IsTrue(result.ConfidenceScore >= 75m);
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

    private static EmailIngestionAccount CreateAccount()
    {
        return EmailIngestionAccount.Create(
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
    }

    private static EmailMessage CreateAttachmentOnlyMessage(
        Guid accountId,
        string externalMessageId
    )
    {
        return EmailMessage.Create(
            accountId,
            externalMessageId,
            10,
            null,
            "Andrea",
            "andrea@example.com",
            "rates@example.com",
            null,
            "Documento adjunto",
            "Adjunto documento.",
            null,
            DateTime.UtcNow,
            true,
            null,
            null
        );
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
