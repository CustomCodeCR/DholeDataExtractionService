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
}
