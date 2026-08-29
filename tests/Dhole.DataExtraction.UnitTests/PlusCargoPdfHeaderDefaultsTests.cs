using Dhole.DataExtraction.Infrastructure.Extraction.Pdf;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class PlusCargoPdfHeaderDefaultsTests
{
    [TestMethod]
    public void InferDocumentHeaderDefaults_ScrambledPlusCargoHeader_RecoversDatesAndAgent()
    {
        const string rawText =
            "26-Aug-2026Effective :CASTRO FALLAS Costa RicaCustomer:\n"
            + "25-Sep-2026Expiration:\n"
            + "Quotation Ref. : m3390\n"
            + "8501 Northwest 17th\n"
            + "Street, Suite 102\n"
            + "Santiago Fioravanti\n"
            + "MAERSK Port Everglades Puerto Moin";

        var defaults = PdfDocumentExtractor.InferDocumentHeaderDefaults(rawText);

        Assert.AreEqual("2026-08-26", defaults.ValidFrom);
        Assert.AreEqual("2026-09-25", defaults.ValidTo);
        Assert.AreEqual("PlusCargo", defaults.Agent);
    }

    [TestMethod]
    public void InferDocumentHeaderDefaults_NormalHeader_RecoversDatesAndExplicitBrand()
    {
        const string rawText =
            "PLUSCARGO\n"
            + "Customer: CASTRO FALLAS Costa Rica Effective : 26-Aug-2026\n"
            + "Expiration: 25-Sep-2026";

        var defaults = PdfDocumentExtractor.InferDocumentHeaderDefaults(rawText);

        Assert.AreEqual("2026-08-26", defaults.ValidFrom);
        Assert.AreEqual("2026-09-25", defaults.ValidTo);
        Assert.AreEqual("PlusCargo", defaults.Agent);
    }
}
