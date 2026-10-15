using Dhole.DataExtraction.Domain.Extraction.Entities;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using Dhole.DataExtraction.Infrastructure.Quality;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class DataQualityValidatorTests
{
    [TestMethod]
    public async Task CatalogValues_WithoutConfigMatch_RemainValidAndPreserveDetectedData()
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
        Assert.AreEqual(0, result.WarningRows);
        Assert.AreEqual(1, result.ValidRows);
        Assert.AreEqual(PricingExtractionRecordStatus.Valid, record.Status);
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

    [TestMethod]
    public async Task AirColoaderWithoutExplicitCarrier_RemainsReviewable()
    {
        var executionId = Guid.NewGuid();
        var record = PricingExtractionRecord.Create(
            executionId,
            Guid.NewGuid(),
            "Pier17 AIR",
            2,
            "PVG",
            "SJO",
            null,
            "AIR",
            null,
            "Eurasia",
            "General Cargo",
            "USD",
            null,
            15,
            new DateTime(2026, 9, 28),
            new DateTime(2026, 10, 4),
            6.06m,
            null,
            null,
            null,
            6.06m,
            null,
            null,
            null,
            null,
            "Consolidado; tarifa por KG/VOL",
            """{ "TariffMode": "AIR", "ServiceMode": "AIR_CONSOLIDATED", "RateBasis": "KG/VOL" }""",
            null
        );

        var result = await new DataQualityValidator().ValidateAsync(executionId, [record]);

        Assert.AreEqual(0, result.InvalidRows);
        Assert.AreEqual(1, result.WarningRows);
        Assert.AreEqual(PricingExtractionRecordStatus.RequiresReview, record.Status);
        Assert.IsTrue(result.Issues.Any(issue =>
            issue.Code == "missing_carrier" && !issue.IsBlocking
        ));
        Assert.IsFalse(result.Issues.Any(issue =>
            issue.Code is "missing_container_type"
                or "missing_valid_from"
                or "missing_valid_to"
                or "missing_rate_amount"
                or "missing_origin_port"
                or "missing_port_of_exit"
        ));
    }


    [TestMethod]
    public async Task AirFreightAliasWithoutExplicitCarrier_RemainsReviewable()
    {
        var executionId = Guid.NewGuid();
        var record = PricingExtractionRecord.Create(
            executionId,
            Guid.NewGuid(),
            "WL AIR",
            2,
            "SZX",
            "MEX",
            null,
            "AIRFREIGHT",
            null,
            "Guangzhou Wanlin International Logistics",
            null,
            "USD",
            null,
            null,
            new DateTime(2026, 9, 28),
            new DateTime(2026, 10, 4),
            7.65m,
            null,
            null,
            null,
            7.65m,
            null,
            null,
            null,
            null,
            "AOL:SZX; AOD:MEX; Rate USD7.65/KG",
            "{}",
            null
        );

        var result = await new DataQualityValidator().ValidateAsync(executionId, [record]);

        Assert.AreEqual(0, result.InvalidRows);
        Assert.AreEqual(1, result.WarningRows);
        Assert.IsTrue(result.Issues.Any(issue =>
            issue.Code == "missing_carrier" && !issue.IsBlocking
        ));
    }


    [TestMethod]
    public async Task CompetitorFclWithoutPublishedCarrierOrStartDate_IsReviewableNotInvalid()
    {
        var executionId = Guid.NewGuid();
        var record = PricingExtractionRecord.Create(
            executionId, Guid.NewGuid(), "PDF Competitor FCL - Logica", 1,
            "Ningbo", "Caldera", "Heredia", "40HC", null, "LOGICA",
            "General Cargo", "USD", null, 40, null,
            new DateTime(2026, 10, 21), 7260m, null, 847.8m, 695m,
            null, 8802.8m, null, null, null,
            "El PDF no publica naviera ni inicio de vigencia",
            """{"MissingExplicitValidFrom":"true","RequiresManualReview":"true"}""", null);

        var result = await new DataQualityValidator().ValidateAsync(executionId, [record]);

        Assert.AreEqual(0, result.InvalidRows);
        Assert.AreEqual(1, result.WarningRows);
        Assert.AreEqual(PricingExtractionRecordStatus.RequiresReview, record.Status);
        Assert.IsTrue(result.Issues.Any(issue =>
            issue.Code == "missing_carrier" && !issue.IsBlocking));
        Assert.IsTrue(result.Issues.Any(issue =>
            issue.Code == "missing_valid_from" && !issue.IsBlocking));
        Assert.IsTrue(result.Issues.Any(issue =>
            issue.Code == "source_requires_manual_review" && !issue.IsBlocking));
    }

    [TestMethod]
    public async Task SuspiciousMgfRate_IsNotAutoApproved()
    {
        var executionId = Guid.NewGuid();
        var record = PricingExtractionRecord.Create(
            executionId, Guid.NewGuid(), "PDF Competitor FCL - MGF", 2,
            "Shanghai", "Moin", null, "40HC", "EVERGREEN", "MGF",
            "General Cargo", "USD", 14, 48, new DateTime(2026, 10, 15),
            new DateTime(2026, 10, 21), 1245m, null, 615m, 1005m,
            null, null, null, null, null, "Anomalía de precio",
            """{"RequiresManualReview":"true"}""", null);

        var result = await new DataQualityValidator().ValidateAsync(executionId, [record]);

        Assert.AreEqual(0, result.InvalidRows);
        Assert.AreEqual(1, result.WarningRows);
        Assert.AreEqual(PricingExtractionRecordStatus.RequiresReview, record.Status);
        Assert.IsTrue(result.Issues.Any(issue =>
            issue.Code == "source_requires_manual_review" && !issue.IsBlocking));
    }

}
