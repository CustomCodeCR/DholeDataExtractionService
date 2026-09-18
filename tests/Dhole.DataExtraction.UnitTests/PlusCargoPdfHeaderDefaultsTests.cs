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
    [TestMethod]
    public void InferDocumentHeaderDefaults_Pier17SpanishLcl_RecoversValidityModalityAndCurrency()
    {
        const string rawText = """
            TARIFARIO LCL DE IMPORTACION
            DEL MUNDO HACIA GUATEMALA
            VALIDEZ: 01 DE SEPTIEMBRE AL 30 DE SEPTIEMBRE DE 2026
            ORIGEN CFS CARGUE TARIFA MIN T/T RUTA
            Argentina Buenos Aires $170.00 $170.00 45 Vía Panamá
            """;

        var defaults = PdfDocumentExtractor.InferDocumentHeaderDefaults(rawText);

        Assert.AreEqual("2026-09-01", defaults.ValidFrom);
        Assert.AreEqual("2026-09-30", defaults.ValidTo);
        Assert.AreEqual("LCL", defaults.ContainerType);
        Assert.AreEqual("USD", defaults.Currency);
    }

    [TestMethod]
    public void InferDocumentHeaderDefaults_Pier17EnglishLcl_RecoversSharedYearRange()
    {
        const string rawText = """
            COUNTRY ORIGIN RATE PER CBM MINIMUM T/T ROUTE
            China Qingdao $165.00 $165.00 35-45 Aprox Direct
            CFS to CFS Rates
            Valid from September 16th to September 30th, 2026.
            """;

        var defaults = PdfDocumentExtractor.InferDocumentHeaderDefaults(rawText);

        Assert.AreEqual("2026-09-16", defaults.ValidFrom);
        Assert.AreEqual("2026-09-30", defaults.ValidTo);
        Assert.AreEqual("LCL", defaults.ContainerType);
        Assert.AreEqual("USD", defaults.Currency);
    }

}
