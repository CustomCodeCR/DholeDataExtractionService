namespace Dhole.DataExtraction.Api.Authorization;

internal static class DataExtractorScopeNames
{
    public const string DevExtractionTest = "data-extraction.dev.extract";
    public const string ExtractionExecute = "data-extraction.extraction.execute";
    public const string ExtractionPreview = "data-extraction.extraction.preview";
    public const string ExtractionValidate = "data-extraction.extraction.validate";

    public const string EmailAccountsView = "data-extraction.email-accounts.view";
    public const string EmailAccountsCreate = "data-extraction.email-accounts.create";
    public const string EmailAccountsUpdate = "data-extraction.email-accounts.update";
    public const string EmailAccountsDelete = "data-extraction.email-accounts.delete";

    public const string EmailMessagesView = "data-extraction.email-messages.view";
    public const string EmailMessagesReprocess = "data-extraction.email-messages.reprocess";
    public const string EmailMessagesIgnore = "data-extraction.email-messages.ignore";

    public const string EmailExtractionsView = "data-extraction.email-extractions.view";
    public const string EmailExtractionsReprocess = "data-extraction.email-extractions.reprocess";
    public const string EmailExtractionsSendToPricing = "data-extraction.email-extractions.send-to-pricing";
}
