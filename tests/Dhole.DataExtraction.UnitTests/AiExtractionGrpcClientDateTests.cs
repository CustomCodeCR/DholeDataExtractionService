using Dhole.DataExtraction.Infrastructure.GrpcClients;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class AiExtractionGrpcClientDateTests
{
    [TestMethod]
    public void ParseAiDate_DayAndEnglishMonthWithoutYear_UsesCurrentYear()
    {
        var year = DateTime.UtcNow.Year;

        Assert.AreEqual(new DateTime(year, 8, 15), AiExtractionGrpcClient.ParseAiDate("15 Aug"));
        Assert.AreEqual(new DateTime(year, 8, 21), AiExtractionGrpcClient.ParseAiDate("21 Aug"));
    }

    [TestMethod]
    public void ParseAiDate_IsoDate_PreservesExplicitYear()
    {
        Assert.AreEqual(new DateTime(2026, 8, 15), AiExtractionGrpcClient.ParseAiDate("2026-08-15"));
        Assert.AreEqual(new DateTime(2026, 8, 20), AiExtractionGrpcClient.ParseAiDate("2026-08-20"));
    }

    [TestMethod]
    public void ParseAiDate_SpanishMonthWithoutYear_IsAccepted()
    {
        var year = DateTime.UtcNow.Year;

        Assert.AreEqual(new DateTime(year, 8, 14), AiExtractionGrpcClient.ParseAiDate("14 ago"));
        Assert.AreEqual(new DateTime(year, 8, 20), AiExtractionGrpcClient.ParseAiDate("20 agosto"));
    }
    [TestMethod]
    public void ParseAiDateRange_WwlValidityWithoutYear_RecoversBothDates()
    {
        var year = DateTime.UtcNow.Year;

        var range = AiExtractionGrpcClient.ParseAiDateRange("15 Aug-21 Aug");

        Assert.AreEqual(new DateTime(year, 8, 15), range.ValidFrom);
        Assert.AreEqual(new DateTime(year, 8, 21), range.ValidTo);
    }

    [TestMethod]
    public void ParseAiDateRange_SharedMonth_RecoversBothDates()
    {
        var year = DateTime.UtcNow.Year;

        var range = AiExtractionGrpcClient.ParseAiDateRange("15-21 Aug");

        Assert.AreEqual(new DateTime(year, 8, 15), range.ValidFrom);
        Assert.AreEqual(new DateTime(year, 8, 21), range.ValidTo);
    }

}
