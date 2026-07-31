namespace Dhole.DataExtraction.Domain.Emails.Enums;

public enum EmailExtractionJobStatus
{
    Pending = 1,
    Extracting = 2,
    AwaitingAi = 3,
    AiProcessing = 4,
    ValidatingAiResult = 5,
    AwaitingPricing = 6,
    SentToPricing = 7,
    NeedsReview = 8,
    Failed = 9,
    Ignored = 10,
}
