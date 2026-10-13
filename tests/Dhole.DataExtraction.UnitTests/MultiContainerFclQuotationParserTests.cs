using System.Text.Json;
using Dhole.DataExtraction.Infrastructure.Extraction.Pdf;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class MultiContainerFclQuotationParserTests
{
    private const string Quotation = """
        TARIFA FCL
        Señores : IMPORTADORA DE TEXTILES                    12/10/2026
        ORIGEN: NINGBO
        DESTINO: HEREDIA ALMACEN FISCAL     POL: NINGBO
        TIEMPO DE TRANSITO*: 38-45 DIAS APROX     POD : CALDERA
        INCOTERM: FOB      CARRIER: MSC
        COMMODITY: CARGA GENERAL NO PELIGROSA / FAK / 14 FREETIME
        CONCEPTO COSTO UT CALCULO POL IVA TOTAL
        FLETE MARITIMO $ 7,100.00 40 HQ 3 CNTR $ 21,300.00
        MANEJOS EN CR $ 100.00 1 BL $13.00 $100.00
        INTERNO $ 750.00 3 CNTR $2,250.00 $292.50 $2,542.50
        CARGOS DE NAVIERA $ 550.00 3 CNTR $1,650.00 $214.50 $1,864.50
        ingreso a predio -transitorio $ 290.00 3 CNTR $870.00 $113.10 $983.10
        REDESTINO $ 95.00 3 CNTR $285.00 $37.05 $322.05
        Considerar que los puertos están saturados   MONTO GRAVADO $1,785.00
        Información del puerto IVA $670.15
        Aproximadas TOTAL DOLARES IVA INCLUIDO $ 27,112.15
        VALIDEZ AL 21 DE OCTUBRE 2026
        ADICIONALES: REDESTINO: $95.00
        """;

    [TestMethod]
    public void Import863_Three40Hq_ProducesOneComparableUnitRateAndFlagsInvoiceMismatch()
    {
        var extracted = MultiContainerFclQuotationParser.TryParse(Quotation.Split('\n'));
        Assert.AreEqual(1, extracted.Count);
        var row = extracted[0].Rows.Single().Values;
        Assert.AreEqual("3", row["Quantity"]);
        Assert.AreEqual("40HC", row["ContainerType"]);
        Assert.AreEqual("NINGBO", row["OriginPort"]);
        Assert.AreEqual("CALDERA", row["PortOfExit"]);
        Assert.AreEqual("HEREDIA ALMACEN FISCAL", row["DestinationPort"]);
        Assert.AreEqual("MSC", row["Carrier"]);
        Assert.AreEqual("7100", row["OceanFreight"]);
        Assert.AreEqual("750", row["Surcharges"]);
        Assert.AreEqual("968.3333", row["DestinationCharges"]);
        Assert.AreEqual("8818.3333", row["TotalSale"]);
        Assert.AreEqual("14", row["FreeDays"]);
        Assert.AreEqual("45", row["TransitDays"]);
        Assert.AreEqual("2026-10-12", row["ValidFrom"]);
        Assert.AreEqual("2026-10-21", row["ValidTo"]);
        Assert.AreEqual("26455", row["ReconstructedPreTaxTotal"]);
        Assert.AreEqual("27112.15", row["QuotedInvoiceTotal"]);
        Assert.AreEqual("13", row["InvoiceDiscrepancy"]);
        Assert.AreEqual("true", row["RequiresManualReview"]);

        using var raw = JsonDocument.Parse(extracted[0].Rows.Single().RawJson!);
        using var lineItems = JsonDocument.Parse(raw.RootElement.GetProperty("LineItemsJson").GetString()!);
        Assert.AreEqual(6, lineItems.RootElement.GetArrayLength());
        Assert.AreEqual("PerBillOfLading", lineItems.RootElement[1].GetProperty("Basis").GetString());
        Assert.AreEqual(1, lineItems.RootElement[1].GetProperty("Quantity").GetInt32());
    }

    [TestMethod]
    public void CorrectedVatInclusiveTotal_IsComparableWithoutReview()
    {
        var corrected = Quotation.Replace("27,112.15", "27,125.15", StringComparison.Ordinal);
        var rows = MultiContainerFclQuotationParser.TryParse(corrected.Split('\n'));
        Assert.AreEqual("false", rows.Single().Rows.Single().Values["RequiresManualReview"]);
    }

    [TestMethod]
    public void ContainerQuantityOne_IsNotMisclassifiedAsMultiContainer()
    {
        var single = Quotation.Replace("40 HQ 3 CNTR", "40 HQ 1 CNTR", StringComparison.Ordinal);
        Assert.AreEqual(0, MultiContainerFclQuotationParser.TryParse(single.Split('\n')).Count);
    }

    [TestMethod]
    public void PerContainerLineCountMismatch_FallsBackToSafeExtraction()
    {
        var conflicting = Quotation.Replace("INTERNO $ 750.00 3 CNTR", "INTERNO $ 750.00 2 CNTR",
            StringComparison.Ordinal);
        Assert.AreEqual(0, MultiContainerFclQuotationParser.TryParse(conflicting.Split('\n')).Count);
    }
}
