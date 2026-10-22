using Dhole.DataExtraction.Domain.Emails;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using Dhole.DataExtraction.Infrastructure.Email;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class EmailSubjectIgnorePolicyTests
{
    [TestMethod]
    [DataRow("Delivery Status Notification (Failure)", true)]
    [DataRow(" delivery status notification (failure) ", true)]
    [DataRow("DELIVERY STATUS NOTIFICATION (FAILURE)", true)]
    [DataRow("Delivery Status Notification (Delay)", true)]
    [DataRow(" delivery status notification (delay) ", true)]
    [DataRow("DELIVERY STATUS NOTIFICATION (DELAY)", true)]
    [DataRow("Delivery Status Notification (Success)", false)]
    [DataRow("Re: Delivery Status Notification (Failure)", false)]
    [DataRow("Tarifas y cotización", false)]
    [DataRow(null, false)]
    public void ShouldIgnore_OnlyMatchesFailureNotificationSubject(string? subject, bool expected)
    {
        Assert.AreEqual(expected, EmailSubjectIgnorePolicy.ShouldIgnore(subject));
    }

    [TestMethod]
    [DataRow("Tarifas marítimas", "mailer-daemon@googlemail.com", true)]
    [DataRow("Undelivered Mail Returned to Sender", "MAILER-DAEMON@example.com", true)]
    [DataRow("Mail delivery failed", "postmaster@example.com", true)]
    [DataRow("Tarifas marítimas", "agent@example.com", false)]
    [DataRow("Re: Delivery Status Notification (Delay)", "agent@example.com", false)]
    [DataRow("Tarifas marítimas", null, false)]
    public void ShouldIgnore_RecognizesAutomatedSendersWithoutIgnoringLegitimateTariffs(
        string subject, string? fromAddress, bool expected)
    {
        Assert.AreEqual(expected, EmailSubjectIgnorePolicy.ShouldIgnore(subject, fromAddress));
    }

    [TestMethod]
    [DataRow("Delivery Status Notification (Failure)", "mailer-daemon@example.com")]
    [DataRow("Delivery Status Notification (Delay)", "mailer-daemon@googlemail.com")]
    [DataRow("Tarifas marítimas", "mailer-daemon@googlemail.com")]
    public void Classifier_DeliveryNotificationWithPricingAttachment_NeverQueuesExtraction(string subject, string fromAddress)
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
            "imap:rates@example.com:123",
            123,
            null,
            "Mail Delivery Subsystem",
            fromAddress,
            "rates@example.com",
            null,
            subject,
            "Tarifas FCL USD 5000 POL SHANGHAI POD CALDERA vigencia 20/10/2026",
            null,
            DateTime.UtcNow,
            true,
            null,
            null
        );
        var attachment = EmailAttachment.Create(
            message.Id,
            "undelivered-tarifas.pdf",
            "application/pdf",
            ".pdf",
            100,
            "sha256-123",
            "storage/undelivered-tarifas.pdf",
            SourceFileType.Pdf
        );

        var classification = new EmailRateClassifier().Classify(message, [attachment], account);

        Assert.IsFalse(classification.ContainsRates);
        Assert.IsFalse(classification.ProcessBody);
        Assert.AreEqual(0, classification.AttachmentIdsToProcess.Count);
        Assert.Contains("ignorado", classification.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [TestMethod]
    [DataRow("Dhole System")]
    [DataRow("dhole system")]
    [DataRow("Dhole")]
    public void ShouldIgnore_DirectDholeSystemSender(string senderName)
    {
        Assert.IsTrue(EmailSubjectIgnorePolicy.ShouldIgnore(
            "Actualización de una tarifa",
            "notifications@example.com",
            senderName
        ));
    }

    [TestMethod]
    [DataRow("Tarifa Lista - QUO-00000-000327")]
    [DataRow("RE: Tarifa Lista - QUO-00000-000327 - Cliente")]
    [DataRow("RV: RE: Tarifa enviada - QUO-00000-000343")]
    public void ShouldIgnore_DholeNotificationSubjectAndReplies(string subject)
    {
        Assert.IsTrue(EmailSubjectIgnorePolicy.ShouldIgnore(
            subject, "jmesen@castrofallas.com", "Jose Pablo Mesen"
        ));
    }

    [TestMethod]
    public void ShouldIgnore_RepliesQuotingDholeSystemEvenWithRateAttachments()
    {
        var body = """
            Buenos días, recibido.
            De: Dhole System <notificaciones@example.com>
            Enviado: jueves, 22 de octubre de 2026
            Asunto: Cotización procesada

            Tarifa FCL USD 5000, POL SHANGHAI, POE CALDERA.
            """;
        Assert.AreEqual(
            EmailSubjectIgnorePolicy.IgnoredDholeSystemReason,
            EmailSubjectIgnorePolicy.GetIgnoreReason(
                "RE: Cotización procesada", "pricing@example.com", "Pricing",
                body
            )
        );
    }

    [TestMethod]
    public void ShouldIgnore_HtmlQuotedDholeSystemSender()
    {
        Assert.IsTrue(EmailSubjectIgnorePolicy.ShouldIgnore(
            "RV: Aviso",
            "person@example.com",
            "Persona",
            null,
            "<div>Respuesta</div><div>De: <b>Dhole System</b> &lt;notify@example.com&gt;</div>"
        ));
    }

    [TestMethod]
    public void ShouldNotIgnore_LegitimateTariffJustMentioningDhole()
    {
        Assert.IsFalse(EmailSubjectIgnorePolicy.ShouldIgnore(
            "RE: Tarifa Caldera",
            "carrier@example.com",
            "Agente",
            "Pueden cargar esta tarifa en Dhole System? Adjunto USD 5000."
        ));
        Assert.IsFalse(EmailSubjectIgnorePolicy.ShouldIgnore(
            "RE: Tarifa enviada",
            "carrier@example.com",
            "Agente",
            "Tarifa nueva de Caldera, 40HC."
        ));
    }

    [TestMethod]
    public void Classifier_DholeSystemReplyWithPricingAttachment_NeverQueuesExtraction()
    {
        var account = EmailIngestionAccount.Create(
            "Tarifas", "rates@example.com", EmailProviderType.Gmail,
            null, 993, true, "rates@example.com", "DATA_EXTRACTION_EMAIL_PASSWORD",
            "INBOX", 5, true, true, 90m, true, true, "*", null
        );
        var message = EmailMessage.Create(
            account.Id, "imap:rates@example.com:124", 124, null,
            "Marco Artavia", "pricing@castrofallas.com", "rates@example.com",
            null, "RE: Tarifa Lista - QUO-00000-000327",
            "Adjunto tarifa FCL USD 5000, POL SHANGHAI, POD CALDERA.",
            null, DateTime.UtcNow, true, null, null
        );
        var attachment = EmailAttachment.Create(
            message.Id, "tarifa.pdf", "application/pdf", ".pdf",
            100, "sha256-dhole-system", "storage/tarifa.pdf", SourceFileType.Pdf
        );

        var result = new EmailRateClassifier().Classify(message, [attachment], account);

        Assert.IsFalse(result.ContainsRates);
        Assert.IsFalse(result.ProcessBody);
        Assert.HasCount(0, result.AttachmentIdsToProcess);
        Assert.AreEqual(EmailSubjectIgnorePolicy.IgnoredDholeSystemReason, result.Reason);
    }

}
