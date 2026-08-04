using Dhole.DataExtraction.Domain.Extraction.Entities;

namespace Dhole.DataExtraction.Application.Abstractions.Services;

public interface IEmailAgentResolver
{
    Task ApplyFromEmailAsync(
        IReadOnlyCollection<PricingExtractionRecord> records,
        string? subject,
        string? bodyText,
        string? bodyHtml,
        Guid? updatedBy = null,
        CancellationToken cancellationToken = default
    );
}
