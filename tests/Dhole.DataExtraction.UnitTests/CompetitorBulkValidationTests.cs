using Dhole.DataExtraction.Domain.Extraction.Entities;
using Dhole.DataExtraction.Infrastructure.Quality;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class CompetitorBulkValidationTests
{
    [TestMethod]
    public async Task CompetitorFcl_ManyRows_KeepSourceDataWithoutDuplicateCatalogIssues()
    {
        var executionId = Guid.NewGuid();
        var records = Enumerable.Range(1, 108).Select(index =>
            PricingExtractionRecord.Create(
                executionId, Guid.NewGuid(), "PDF Competitor FCL - MGF", index,
                "QINGDAO", "CALDERA", null, "40HC", "MSC", "MGF",
                "General Cargo", "USD", 10, 48,
                DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(4),
                7665m, null, 810m, 495m, null, null, null, null,
                null, "Tarifario competencia", "{}", null)).ToArray();
        var result = await new DataQualityValidator().ValidateAsync(executionId, records);
        Assert.AreEqual(108, result.TotalRows);
        Assert.AreEqual(108, result.ValidRows);
        Assert.AreEqual(0, result.WarningRows);
        Assert.AreEqual(0, result.InvalidRows);
        Assert.HasCount(0, result.Issues.Where(i => i.Code.StartsWith("unknown_")));
        Assert.IsTrue(records.All(row => row.OriginPort == "QINGDAO"
            && row.OceanFreight == 7665m));
    }

    [TestMethod]
    public async Task CompetitorFcl_AnomalousPrintedAmount_StillRequiresManualReview()
    {
        var id = Guid.NewGuid();
        var record = PricingExtractionRecord.Create(
            id, Guid.NewGuid(), "PDF Competitor FCL - MGF", 1,
            "SHANGHAI", "MOIN", null, "40HC", "EVERGREEN", "MGF",
            "General Cargo", "USD", 14, 48,
            DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddDays(4),
            1245m, null, 1005m, 480m, null, null, null, null,
            null, "Importe original sospechoso",
            """{"RequiresManualReview":"true"}""", null);
        var result = await new DataQualityValidator().ValidateAsync(id, [record]);
        Assert.AreEqual(1, result.WarningRows);
        Assert.IsTrue(result.Issues.Any(i => i.Code == "source_requires_manual_review"));
        Assert.AreEqual(1245m, record.OceanFreight);
    }
}
