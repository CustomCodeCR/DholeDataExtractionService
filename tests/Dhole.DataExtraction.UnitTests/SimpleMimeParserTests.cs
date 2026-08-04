using System.Text;
using Dhole.DataExtraction.Infrastructure.Email;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class SimpleMimeParserTests
{
    [TestMethod]
    public void ParseRawMessageFallback_PreservesRateBodyFromMalformedMime()
    {
        const string raw = """
            From: Agent Rates <rates@example.com>
            To: Royner <royner@example.com>
            Subject: CASTRO FALLS / WWL CONTRACT
            Date: Mon, 3 Aug 2026 13:00:00 -0600
            Message-ID: <uid66@example.com>
            Content-Type: multipart/mixed; boundary="broken-boundary"

            Dear Royner,
            Pls consider rate USD6300/6400, valid 8-14/Aug Carrier MSC/ONE NAC.
            POL: Shanghai/Kaohsiung/Shekou/Qingdao/Ningbo
            POD: Acajutla/Corinto/Caldera
            COMM: Auto Spare Parts
            """;

        var parsed = SimpleMimeParser.ParseRawMessageFallback(
            Encoding.UTF8.GetBytes(raw.Replace("\n", "\r\n", StringComparison.Ordinal)),
            "imap:rates@example.com:66",
            66
        );

        Assert.AreEqual(66L, parsed.Uid);
        Assert.AreEqual("CASTRO FALLS / WWL CONTRACT", parsed.Subject);
        StringAssert.Contains(parsed.BodyText ?? string.Empty, "USD6300/6400");
        StringAssert.Contains(parsed.BodyText ?? string.Empty, "Shanghai/Kaohsiung");
        Assert.AreEqual("rates@example.com", parsed.FromAddress);
        Assert.AreEqual(0, parsed.Attachments.Count);
    }

    [TestMethod]
    public void ParseRawMessageFallback_RemovesLargeBase64BlocksButKeepsText()
    {
        var binaryLines = string.Join("\r\n", Enumerable.Repeat(new string('A', 76), 8));
        var raw = $"""
            From: rates@example.com
            Subject: FAK AUG
            Content-Type: text/plain

            Rate USD 7500 for 40HQ
            {binaryLines}
            Valid 8 Aug-14 Aug
            """;

        var parsed = SimpleMimeParser.ParseRawMessageFallback(
            Encoding.ASCII.GetBytes(raw.Replace("\n", "\r\n", StringComparison.Ordinal)),
            "imap:rates@example.com:66",
            66
        );

        StringAssert.Contains(parsed.BodyText ?? string.Empty, "Rate USD 7500 for 40HQ");
        StringAssert.Contains(parsed.BodyText ?? string.Empty, "Valid 8 Aug-14 Aug");
        StringAssert.Contains(parsed.BodyText ?? string.Empty, "contenido binario MIME omitido");
    }
}
