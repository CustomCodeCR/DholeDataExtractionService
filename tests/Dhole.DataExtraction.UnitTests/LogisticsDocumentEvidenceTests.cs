using Dhole.DataExtraction.Infrastructure.Documentation;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class LogisticsDocumentEvidenceTests
{
    [TestMethod]
    public void Invoice_ExtractsOnlyLabeledSourceValues_AndRequiresReview()
    {
        var source = """
            COMMERCIAL INVOICE
            Invoice No: INV-2026-77
            Shipper: Exportadora Ejemplo
            Consignee: Importadora Ejemplo
            Total Amount: USD 1,200.00
            """;
        var parsed = LogisticsDocumentEvidenceExtractor.Analyze(source, "invoice.pdf");
        Assert.AreEqual("CommercialInvoice", parsed.DocumentType);
        Assert.IsTrue(parsed.RequiresHumanReview);
        Assert.IsTrue(parsed.Fields.Any(f => f.Key == "invoiceNumber" && f.Value == "INV-2026-77"));
        Assert.IsFalse(parsed.Fields.Any(f => f.Key == "containerNumber"));
    }

    [TestMethod]
    public void ContradictoryConsignee_ProducesReviewWarning_AndNeverOverwritesEvidence()
    {
        var parsed = LogisticsDocumentEvidenceExtractor.Analyze(
            "BILL OF LADING\nConsignee: ABC\nConsignee: XYZ\n", "bl.pdf");
        Assert.AreEqual("BillOfLading", parsed.DocumentType);
        Assert.AreEqual("ABC", parsed.Fields.Single(f => f.Key == "consignee").Value);
        Assert.IsTrue(parsed.Warnings.Any(w => w.Contains("Conflicto en consignee")));
    }

    [TestMethod]
    public void ContainerCheckDigit_IsVerifiedBeforeTrustingOcr()
    {
        Assert.IsTrue(LogisticsDocumentEvidenceExtractor.IsValidIso6346("CSQU3054383"));
        Assert.IsFalse(LogisticsDocumentEvidenceExtractor.IsValidIso6346("CSQU3054384"));
    }

    [TestMethod]
    public void SupportedLogisticsDocuments_AreClassified()
    {
        Assert.AreEqual("HBL", LogisticsDocumentEvidenceExtractor.IdentifyType("HOUSE BILL OF LADING"));
        Assert.AreEqual("MBL", LogisticsDocumentEvidenceExtractor.IdentifyType("MASTER B/L"));
        Assert.AreEqual("AWB", LogisticsDocumentEvidenceExtractor.IdentifyType("AIR WAYBILL"));
        Assert.AreEqual("CartaPorte", LogisticsDocumentEvidenceExtractor.IdentifyType("CARTA DE PORTE"));
        Assert.AreEqual("PackingList", LogisticsDocumentEvidenceExtractor.IdentifyType("PACKING LIST"));
        Assert.AreEqual("CargoManifest", LogisticsDocumentEvidenceExtractor.IdentifyType("MANIFIESTO DE CARGA"));
    }
}
