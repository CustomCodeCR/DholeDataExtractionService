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
}
