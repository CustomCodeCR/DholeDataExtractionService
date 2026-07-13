using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Infrastructure.Email;
using Microsoft.Extensions.Configuration;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class EmailSecretResolverTests
{
    [TestMethod]
    public void ResolvePassword_ReadsSecretByReference()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["DATA_EXTRACTION_EMAIL_PASSWORD"] = "abcd efgh ijkl mnop",
                }
            )
            .Build();

        var password = new EmailSecretResolver(configuration).ResolvePassword(CreateAccount());

        Assert.AreEqual("abcdefghijklmnop", password);
    }

    [TestMethod]
    public void ResolvePassword_ReadsSecretFromEmailIngestionSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["EmailIngestion:Secrets:DATA_EXTRACTION_EMAIL_PASSWORD"] =
                        "abcdefghijklmnop",
                }
            )
            .Build();

        var password = new EmailSecretResolver(configuration).ResolvePassword(CreateAccount());

        Assert.AreEqual("abcdefghijklmnop", password);
    }

    private static EmailIngestionAccount CreateAccount()
    {
        return EmailIngestionAccount.Create(
            "Correo de extracción",
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
            false,
            "*",
            null
        );
    }
}
