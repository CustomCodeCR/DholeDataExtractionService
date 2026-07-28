using Dhole.DataExtraction.Contracts.Extraction;
using Dhole.DataExtraction.Infrastructure.Email;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class EmailRateClassifierTests
{
    [TestMethod]
    public void OptionalAgentAndExpiredRateIssues_KeepBodyAtReviewThreshold()
    {
        var rowId = Guid.NewGuid();
        var response = CreateResponse(
            [
                CreateIssue(rowId, "missing_agent", false),
                CreateIssue(rowId, "expired_rate", false),
            ]
        );

        var confidence = new EmailRateClassifier().CalculateExtractionConfidence(
            response,
            null!,
            null
        );

        Assert.AreEqual(90m, confidence);
    }

    [TestMethod]
    public void UnknownConfigValue_RemainsReviewableWithoutZeroingConfidence()
    {
        var rowId = Guid.NewGuid();
        var response = CreateResponse([CreateIssue(rowId, "unknown_carrier", true)]);

        var confidence = new EmailRateClassifier().CalculateExtractionConfidence(
            response,
            null!,
            null
        );

        Assert.AreEqual(90m, confidence);
    }

    [TestMethod]
    public void StructuralBlockingIssue_PreventsAutomaticSend()
    {
        var rowId = Guid.NewGuid();
        var response = CreateResponse([CreateIssue(rowId, "missing_origin_port", true)]);

        var confidence = new EmailRateClassifier().CalculateExtractionConfidence(
            response,
            null!,
            null
        );

        Assert.AreEqual(0m, confidence);
    }

    private static ExtractPricingDataResponse CreateResponse(
        IReadOnlyCollection<ExtractionIssueDto> issues
    )
    {
        return new ExtractPricingDataResponse(
            true,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid().ToString(),
            new ExtractionSummaryDto(1, 0, 0, 1, true),
            null,
            [],
            issues,
            null,
            null,
            null
        );
    }

    private static ExtractionIssueDto CreateIssue(Guid rowId, string code, bool isBlocking)
    {
        return new ExtractionIssueDto(
            Guid.NewGuid(),
            Guid.NewGuid(),
            rowId,
            code,
            code,
            isBlocking,
            "Rates",
            2,
            null,
            null
        );
    }
}
