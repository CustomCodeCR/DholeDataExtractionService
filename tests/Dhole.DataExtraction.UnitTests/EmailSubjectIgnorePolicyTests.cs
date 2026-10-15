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
    [DataRow("Delivery Status Notification (Success)", false)]
    [DataRow("Re: Delivery Status Notification (Failure)", false)]
    [DataRow("Tarifas y cotización", false)]
    [DataRow(null, false)]
    public void ShouldIgnore_OnlyMatchesFailureNotificationSubject(string? subject, bool expected)
    {
        Assert.AreEqual(expected, EmailSubjectIgnorePolicy.ShouldIgnore(subject));
    }

    [TestMethod]
    public void Classifier_DeliveryFailureWithPricingAttachment_NeverQueuesExtraction()
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
            "mailer-daemon@example.com",
            "rates@example.com",
            null,
            "Delivery Status Notification (Failure)",
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
