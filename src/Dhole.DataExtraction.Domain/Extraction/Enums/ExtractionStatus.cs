namespace Dhole.DataExtraction.Domain.Extraction.Enums;

public enum ExtractionStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    CompletedWithIssues = 3,
    Failed = 4,
}
