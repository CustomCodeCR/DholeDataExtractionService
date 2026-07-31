using Dhole.DataExtraction.Infrastructure.Normalization;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class PricingCurrencyNormalizerTests
{
    [TestMethod]
    [DataRow(null, "USD")]
    [DataRow("", "USD")]
    [DataRow("$6,430", "USD")]
    [DataRow("US$", "USD")]
    [DataRow("EUR", "EUR")]
    [DataRow("€ 1.200", "EUR")]
    [DataRow("₡ 1000", "CRC")]
    [DataRow("RMB", "CNY")]
    public void NormalizeOrDefault_ReturnsExpectedCurrency(string? source, string expected)
    {
        Assert.AreEqual(expected, PricingCurrencyNormalizer.NormalizeOrDefault(source));
    }

    [TestMethod]
    public void TryNormalizeExplicit_WhenValueIsOnlyNumeric_ReturnsNull()
    {
        Assert.IsNull(PricingCurrencyNormalizer.TryNormalizeExplicit("6,430"));
    }
}
