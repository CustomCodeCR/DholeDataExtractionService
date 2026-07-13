namespace Dhole.DataExtraction.Domain.Emails.Enums;

public enum EmailMessageStatus
{
    Received = 1,
    Queued = 2,
    Processing = 3,
    Extracted = 4,
    NeedsReview = 5,
    Ignored = 6,
    Duplicated = 7,
    Failed = 8,
}
