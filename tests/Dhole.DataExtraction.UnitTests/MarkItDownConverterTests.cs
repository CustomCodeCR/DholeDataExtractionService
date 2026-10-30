using Dhole.DataExtraction.Infrastructure.Markdown;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class MarkItDownConverterTests
{
    [TestMethod]
    public async Task Disabled_UsesExistingReaderWithoutInvokingPython()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["MarkItDown:Enabled"] = "false",
                ["MarkItDown:PythonExecutable"] = "/not/installed/python"
            }).Build();
        var converter = new MarkItDownConverter(
            configuration, NullLogger<MarkItDownConverter>.Instance);

        Assert.IsNull(await converter.ConvertEmailBodyAsync(
            "POL Shanghai, freight USD 1500", "<table><tr><td>USD 1500</td></tr></table>"));
    }

    [TestMethod]
    public async Task UnsupportedBinaries_DoNotStartPython()
    {
        var configuration = new ConfigurationBuilder().Build();
        var converter = new MarkItDownConverter(
            configuration, NullLogger<MarkItDownConverter>.Instance);

        Assert.IsNull(await converter.ConvertAsync(
            [1, 2, 3], "attachment.exe", "application/octet-stream", ".exe"));
    }

    [TestMethod]
    public async Task OversizedAttachments_FallBackWithoutAttemptingConversion()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["MarkItDown:MaxInputBytes"] = "5"
            }).Build();
        var converter = new MarkItDownConverter(
            configuration, NullLogger<MarkItDownConverter>.Instance);

        Assert.IsNull(await converter.ConvertAsync(
            new byte[6], "rates.html", "text/html", ".html"));
    }
}
