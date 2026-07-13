namespace Dhole.DataExtraction.Application.Abstractions.Emails;

public interface IPricingImportClient
{
    Task<PricingImportSubmissionResult> SubmitAsync(
        PricingImportSubmissionRequest request,
        CancellationToken cancellationToken = default
    );
}
