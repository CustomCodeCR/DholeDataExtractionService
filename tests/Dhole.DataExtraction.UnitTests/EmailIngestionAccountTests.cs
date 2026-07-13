using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class EmailIngestionAccountTests
{
    [TestMethod]
    public void GmailAccount_WithEnvironmentVariableReference_IsCreated()
    {
        var account = CreateAccount("DATA_EXTRACTION_EMAIL_PASSWORD");

        Assert.AreEqual("DATA_EXTRACTION_EMAIL_PASSWORD", account.SecretReference);
    }

    [TestMethod]
    public void GmailAccount_WithInlineAppPassword_IsRejectedWithoutEchoingIt()
    {
        const string inlinePassword = "abcdefghijklmnop";

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            () => CreateAccount(inlinePassword)
        );

        StringAssert.DoesNotContain(exception.Message, inlinePassword);
        StringAssert.Contains(exception.Message, "variable de entorno");
    }

    private static EmailIngestionAccount CreateAccount(string secretReference)
    {
        return EmailIngestionAccount.Create(
            "Correo de extracción",
            "rates@example.com",
            EmailProviderType.Gmail,
            null,
            993,
            true,
            "rates@example.com",
            secretReference,
            "INBOX",
            5,
            true,
            true,
            90m,
            true,
            false,
            "*",
            null
        );
    }
}
