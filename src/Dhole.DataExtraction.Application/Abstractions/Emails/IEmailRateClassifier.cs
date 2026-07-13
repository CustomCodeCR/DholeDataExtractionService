using Dhole.DataExtraction.Contracts.Extraction;
using Dhole.DataExtraction.Domain.Emails.Entities;

namespace Dhole.DataExtraction.Application.Abstractions.Emails;

public interface IEmailRateClassifier
{
    EmailClassificationResult Classify(
        EmailMessage message,
        IReadOnlyCollection<EmailAttachment> attachments,
        EmailIngestionAccount account
    );

    decimal CalculateExtractionConfidence(
        ExtractPricingDataResponse response,
        EmailMessage message,
        EmailAttachment? attachment
    );
}
