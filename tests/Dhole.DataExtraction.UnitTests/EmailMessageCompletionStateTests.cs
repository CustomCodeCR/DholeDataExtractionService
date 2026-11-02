using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class EmailMessageCompletionStateTests
{
    [TestMethod]
    public void SuccessfulImport_StaysExtractedDespiteLateSiblingNotifications()
    {
        var message = CreateMessage();
        message.MarkQueued(75m, "Tarifa recibida");
        message.MarkProcessing();
        message.MarkExtracted();

        message.MarkProcessing();
        message.MarkNeedsReview("Otro adjunto sigue en revisión");
        message.MarkFailed("Falló la IA para un adjunto independiente");

        Assert.AreEqual(EmailMessageStatus.Extracted, message.Status);
        Assert.IsNull(message.ErrorMessage);
        Assert.IsNotNull(message.ProcessedAt);
    }

    [TestMethod]
    public void UnsuccessfulImport_CanStillBeMarkedForReviewOrFailure()
    {
        var message = CreateMessage();
        message.MarkProcessing();
        message.MarkNeedsReview("No se extrajeron tarifas");
        Assert.AreEqual(EmailMessageStatus.NeedsReview, message.Status);
        message.MarkFailed("Sin resultados");
        Assert.AreEqual(EmailMessageStatus.Failed, message.Status);
    }

    private static EmailMessage CreateMessage() => EmailMessage.Create(
        Guid.NewGuid(), "email-test", 1L, null, "Supplier",
        "supplier@example.com", "pricing@example.com", null,
        "Tarifas FCL", "Carrier MSC POL Shanghai USD 2500",
        null, DateTime.UtcNow, false, null, null);
}
