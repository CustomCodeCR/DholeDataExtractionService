using Dhole.DataExtraction.Domain.Extraction.Entities;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using Dhole.DataExtraction.Infrastructure.Quality;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class DataQualityValidatorTests
{
    [TestMethod]
    public async Task CatalogValues_WithoutConfigMatch_AreReviewableAndPreserveDetectedData()
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
        Assert.HasCount(5, result.Issues);
        Assert.HasCount(0, result.Issues.Where(issue => issue.IsBlocking));
        Assert.IsTrue(result.Issues.All(issue => issue.Code.StartsWith("unknown_")));
        Assert.IsFalse(result.Issues.Any(issue => issue.Code == "unknown_destination_port"));
        Assert.IsFalse(result.Issues.Any(issue => issue.Code == "unknown_agent"));
    }

    [TestMethod]
    public async Task MissingPodAndAgent_DoNotCreateReviewIssues_WhilePoeRemainsRequired()
    {
        var executionId = Guid.NewGuid();
        var record = PricingExtractionRecord.Create(
            executionId,
            Guid.NewGuid(),
            "Rates",
            2,
            "SHANGHAI",
            "CALDERA",
            null,
            "40HC",
            "MSC",
            null,
            null,
            "USD",
            7,
            22,
            DateTime.UtcNow.Date,
            DateTime.UtcNow.Date.AddDays(30),
            1200m,
            null,
            null,
            null,
            1200m,
            null,
            null,
            null,
            null,
            null,
            "{}",
            null
        );

        var result = await new DataQualityValidator().ValidateAsync(executionId, [record]);

        Assert.IsFalse(result.Issues.Any(issue => issue.Code == "missing_destination_port"));
        Assert.IsFalse(result.Issues.Any(issue => issue.Code == "missing_agent"));
        Assert.IsFalse(result.Issues.Any(issue => issue.Code == "unknown_destination_port"));
        Assert.IsFalse(result.Issues.Any(issue => issue.Code == "unknown_agent"));
        Assert.IsFalse(result.Issues.Any(issue => issue.Code == "same_poe_and_pod"));
        Assert.IsFalse(result.Issues.Any(issue => issue.Code == "missing_port_of_exit"));
        Assert.AreEqual(0, result.InvalidRows);
    }
    [TestMethod]
    public async Task LclWithoutExplicitPoeOrCarrier_RemainsReviewable()
    {
        var executionId = Guid.NewGuid();
        var record = PricingExtractionRecord.Create(
            executionId,
            Guid.NewGuid(),
            "Pier17 LCL",
            2,
            "QINGDAO",
            null,
            null,
            "LCL",
            null,
            null,
            "General Cargo",
            "USD",
            null,
            40,
            new DateTime(2026, 9, 16),
            new DateTime(2026, 9, 30),
            165m,
            null,
            null,
            null,
            165m,
            null,
            null,
            null,
            null,
            "Direct; Rate per CBM; Minimum USD 165",
            "{}",
            null
        );

        var result = await new DataQualityValidator().ValidateAsync(executionId, [record]);

        Assert.AreEqual(0, result.InvalidRows);
        Assert.AreEqual(1, result.WarningRows);
        Assert.IsTrue(result.Issues.Any(issue =>
            issue.Code == "missing_port_of_exit" && !issue.IsBlocking
        ));
        Assert.IsTrue(result.Issues.Any(issue =>
            issue.Code == "missing_carrier" && !issue.IsBlocking
        ));
        Assert.IsFalse(result.Issues.Any(issue =>
            issue.Code is "missing_container_type"
                or "missing_valid_from"
                or "missing_valid_to"
                or "missing_rate_amount"
                or "missing_origin_port"
        ));
    }

}
