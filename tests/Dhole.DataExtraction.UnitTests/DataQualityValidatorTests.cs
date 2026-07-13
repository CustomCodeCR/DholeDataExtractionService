using Dhole.DataExtraction.Domain.Extraction.Entities;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using Dhole.DataExtraction.Infrastructure.Quality;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class DataQualityValidatorTests
{
    [TestMethod]
    public async Task ExtractedCatalogValues_WithoutConfigMatch_RequireReviewInsteadOfBecomingInvalid()
    {
        var executionId = Guid.NewGuid();
        var record = PricingExtractionRecord.Create(
            executionId,
            Guid.NewGuid(),
            "Rates",
            2,
            "SHANGHAI",
            "SHANGHAI",
            "CALDERA",
            "40HC",
            "MAERSK",
            "WWL",
            "General",
            "USD",
            7,
            22,
            DateTime.UtcNow.Date,
            DateTime.UtcNow.Date.AddDays(30),
            1200m,
            100m,
            75m,
            25m,
            1400m,
            1600m,
            200m,
            12.5m,
            null,
            null,
            "{}",
            null
        );

        var result = await new DataQualityValidator().ValidateAsync(executionId, [record]);

        Assert.AreEqual(0, result.InvalidRows);
        Assert.AreEqual(1, result.WarningRows);
        Assert.AreEqual(PricingExtractionRecordStatus.RequiresReview, record.Status);
        Assert.AreEqual(7, result.Issues.Count);
        Assert.IsTrue(result.Issues.All(issue => !issue.IsBlocking));
        Assert.IsTrue(result.Issues.All(issue => issue.Code.StartsWith("unknown_")));
    }
}
