using Dhole.DataExtraction.Contracts.Extraction;
using Dhole.DataExtraction.Workers.Workers;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class EmailExtractionWorkerPolicyTests
{
    [TestMethod]
    [DataRow("EMAIL NAC Narrative")]
    [DataRow("EMAIL FCL Matrix")]
    [DataRow("EMAIL FCL Cell Stream")]
    public void HasCompleteDeterministicEmailMatrix_AcceptsKnownCompleteEmailTables(
        string sourceSheetName
    )
    {
        var executionId = Guid.NewGuid();
        var sourceDocumentId = Guid.NewGuid();
        var response = new ExtractPricingDataResponse(
            true,
            executionId,
            Guid.NewGuid(),
            "policy-test",
            new ExtractionSummaryDto(1, 1, 0, 0, false),
            null,
            [
                new ExtractedPricingRowDto(
                    Guid.NewGuid(),
                    executionId,
                    sourceDocumentId,
                    sourceSheetName,
                    2,
                    "Shanghai",
                    "Caldera",
                    null,
                    "40HC",
                    "MSC",
                    null,
                    "General Cargo",
                    "USD",
                    14,
                    null,
                    new DateTime(2026, 8, 8),
                    new DateTime(2026, 8, 14),
                    7515m,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    "Valid",
                    "{}"
                )
            ],
            [],
            null,
            null
        );

        Assert.IsTrue(
            EmailExtractionWorker.HasCompleteDeterministicEmailMatrix(response)
        );
    }
}
