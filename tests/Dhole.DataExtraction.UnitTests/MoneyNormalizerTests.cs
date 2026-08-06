using Dhole.DataExtraction.Infrastructure.Normalization;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class MoneyNormalizerTests
{
    [DataTestMethod]
    [DataRow("$8 108,00", 8108d)]
    [DataRow("USD 30,000.00 por contenedor", 30000d)]
    [DataRow("18-21 tons - USD 200/20'", 200d)]
    [DataRow("$6,560", 6560d)]
    [DataRow("$8 108,00 $8 316,00 $8 316,00", 8108d)]
    public void Normalize_SelectsOneMonetaryToken(string input, double expected)
    {
        var value = MoneyNormalizer.Normalize(input);

        Assert.IsTrue(value.HasValue);
        Assert.AreEqual((decimal)expected, value.Value);
    }


    [TestMethod]
    public void ToNumeric18Scale4_RejectsOverflowFromDerivedAmounts()
    {
        var value = MoneyNormalizer.ToNumeric18Scale4(399_999_999_999_999m);

        Assert.IsNull(value);
    }

    [TestMethod]
    public void Normalize_RejectsValuesOutsideNumeric18Scale4()
    {
        var value = MoneyNormalizer.Normalize("USD 999999999999999999999.00");

        Assert.IsNull(value);
    }
}
