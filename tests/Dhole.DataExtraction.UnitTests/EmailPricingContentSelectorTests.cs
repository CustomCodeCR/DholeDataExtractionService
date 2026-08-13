using Dhole.DataExtraction.Infrastructure.Email;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class EmailPricingContentSelectorTests
{
    [TestMethod]
    public void ForwardedStackedRateTable_SelectsOnlyActualPricingOffer()
    {
        const string body = """
            Website : https://logisticacastrofallas.com
            AVISO LEGAL: Este mensaje es confidencial.
            ________________________________________________
            De: RSL - Sia Liu (SH) <sialiu.sh@rslog.com>
            Enviado: miércoles, 5 de agosto de 2026 04:02
            Asunto: Request for Indicative Rates

            Dear Marco,
            Good day!
            Pls check below update rates for your ref.
            We expect another increase of USD 500–1,000 after the 15th.
            POL
            POD
            CARRIER
            20'
            40'/40HC
            Free time
            Effective Date
            Expiry date
            Shanghai/ Ningbo
            Caldera
            PIL
            $6,560
            $6,830
            18 days
            8-Aug
            14-Aug
            Best regards,
            Sia Liu
            发件人: Marco Artavia <pricing@castrofallas.com>
            Dear Sia, please share indicative rates for Balboa and Caldera.
            """;

        var selected = EmailPricingContentSelector.SelectBestPricingSection(body);

        StringAssert.Contains(selected, "Pls check below update rates for your ref.");
        StringAssert.Contains(selected, "Shanghai/ Ningbo");
        StringAssert.Contains(selected, "$6,560");
        Assert.IsFalse(selected.Contains("AVISO LEGAL", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(selected.Contains("please share indicative rates", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void StackedTableWithoutIntro_StartsAtPolHeader()
    {
        const string body = """
            POL
            POD
            CARRIER
            20'
            40'/40HC
            Free time
            Effective Date
            Expiry date
            Shanghai
            Caldera
            MSC
            $6,615
            $7,515
            14 days
            8-Aug
            14-Aug
            Best regards,
            """;

        var selected = EmailPricingContentSelector.SelectNewestPricingSection(body);

        Assert.IsTrue(selected.StartsWith("POL", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(selected, "Shanghai");
        Assert.IsFalse(selected.Contains("Best regards", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void RatesUpdate_PreservesImmediateSpaceContextBeforePricingIntro()
    {
        const string body = """
            Dear all,
            Hope you're doing well!
            Currently, space is tight, since vessel schedules are very unstable due to frequent rollovers.
            Please find below the rates for your reference. However, space availability needs to be confirmed on a case-by-case basis locally.
            POL
            POD
            CARRIER
            20'
            Shanghai
            Caldera
            PIL
            $7,600
            Best regards,
            """;

        var selected = EmailPricingContentSelector.SelectNewestPricingSection(body);

        StringAssert.Contains(selected, "space is tight");
        StringAssert.Contains(selected, "Please find below the rates");
        Assert.IsFalse(selected.Contains("Hope you're doing well", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ForwardedThread_OlderLargerTable_DoesNotOverrideNewestSmallerOffer()
    {
        const string body = """
            Website : https://logisticacastrofallas.com
            AVISO LEGAL: Este mensaje es confidencial.
            ---
            De: Veronica.jiang <veronica.jiang@wwl.sg>
            Enviado: miércoles, 12 de agosto de 2026 03:19
            Asunto: UPDATE FAK WWL / CASTRO FALLAS / 12-AUG
            Dear Royner,
            Published FAK for your ref:
            POL
            POD
            CARRIER
            Validity (ETD)
            20'GP
            SHANGHAI
            Puerto Caldera
            PIL
            14 Aug-20 Aug
            $7,700
            Un saludo cordial
            Veronica Jiang
            De: Veronica.jiang <veronica.jiang@wwl.sg>
            Enviado: viernes, 31 de julio de 2026 19:21
            Asunto: UPDATE FAK WWL / CASTRO FALLAS /31-JULY
            Published FAK for your ref:
            POL
            POD
            CARRIER
            Validity (ETD)
            20'GP
            40'GP
            40'HQ
            SHA/NGB/SZN/XMN/TAO/TSN/DLN
            Acajulta/Corinto/Puerto Caldera
            MSC FAK
            8 Aug-14 Aug
            $6,600
            $7,500
            $7,500
            SHA/NGB/SZN/XMN/TAO/TSN/DLN
            Acajulta/Corinto/Puerto Caldera
            MSC Basket
            8 Aug-14 Aug
            $6,500
            $7,300
            $7,300
            SHA/NGB/SZN/XMN/TAO
            Acajulta/Corinto/Puerto Caldera
            ONE FAK
            8 Aug-14 Aug
            $6,900
            $7,200
            $7,200
            SHANGHAI
            Acajulta/Corinto/Puerto Caldera
            PIL
            7 Aug-14 Aug
            $6,700
            $6,900
            $6,900
            """;

        var selected = EmailPricingContentSelector.SelectBestPricingSection(body);

        StringAssert.Contains(selected, "14 Aug-20 Aug");
        StringAssert.Contains(selected, "$7,700");
        Assert.IsFalse(selected.Contains("$6,600", StringComparison.Ordinal));
        Assert.IsFalse(selected.Contains("31 de julio", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void ForwardedThread_MojibakeChineseHeaders_AreRecognizedAsHistoryBoundary()
    {
        const string body = """
            Published FAK for your ref:
            POL
            POD
            CARRIER
            20'GP
            SHANGHAI
            Puerto Caldera
            PIL
            $7,700
            ·¢¼þÈË: Veronica.jiang <veronica.jiang@wwl.sg>
            ·¢ËÍÊ±¼ä: 2026Äê7ÔÂ31ÈÕ 19:21
            Ö÷Ìâ: UPDATE FAK WWL / CASTRO FALLAS /31-JULY
            Published FAK for your ref:
            POL
            POD
            CARRIER
            20'GP
            SHANGHAI
            Puerto Caldera
            PIL
            $6,700
            """;

        var selected = EmailPricingContentSelector.SelectNewestPricingSection(body);

        StringAssert.Contains(selected, "$7,700");
        Assert.IsFalse(selected.Contains("$6,700", StringComparison.Ordinal));
        Assert.IsFalse(selected.Contains("·¢¼þÈË", StringComparison.Ordinal));
    }

}
