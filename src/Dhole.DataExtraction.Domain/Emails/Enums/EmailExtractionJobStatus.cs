namespace Dhole.DataExtraction.Domain.Emails.Enums;

public enum EmailExtractionJobStatus
{
    Pending = 1,
    Processing = 2,
    SentToPricing = 3,
    NeedsReview = 4,
    Failed = 5,
    Ignored = 6,
}
