using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Infrastructure.Extraction.Email;
using Dhole.DataExtraction.Infrastructure.Pipeline;
using System.Text;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class WwlForwardedEmailValidityRepairTests
{
    [TestMethod]
    public async Task RawForwardedEml_WwlStackedTable_ExtractsNewestValidityRange()
    {
        const string currentBody = """
            Website : https://logisticacastrofallas.com
            AVISO LEGAL: Este mensaje es confidencial.
            ________________________________________________
            De: Veronica.jiang <veronica.jiang@wwl.sg>
            Enviado: miercoles, 12 de agosto de 2026 03:19
            Asunto: UPDATE FAK WWL / CASTRO FALLAS / 12-AUG

            Dear Royner,
            Published FAK for your ref:
            FAK
            POL
            POD
            CARRIER
            Free Time
            Validity (ETD)
            20'GP
            40'GP
            40'HQ
            SHA/NGB/SZN/XMN/TAO/TSN/DLN
            Acajulta/Corinto/Puerto Caldera
            MSC FAK
            21 days dry
            15 Aug-21 Aug
            $7,400
            $8,300
            $8,300
            SHA/NGB/SZN/XMN/TAO/TSN/DLN
            Acajulta/Corinto/Puerto Caldera
            MSC Basket
            21 days dry
            15 Aug-21 Aug
            $7,300
            $8,100
            $8,100
            SHA/NGB/SZN/XMN/TAO
            Acajulta/Corinto/Puerto Caldera
            ONE FAK
            16 days dry
            15 Aug-21 Aug
            $7,500
            $7,800
            $7,800
            SHANGHAI
            Acajulta/Corinto/Puerto Caldera
            PIL
            18 days dry
            14 Aug-20 Aug
            $7,700
            $7,900
            $7,900
            Sub to p/s $50/cntr & MBL RLS $75/BILL
            ________________________________________________
            Un saludo cordial
            Veronica Jiang
            De: Veronica.jiang <veronica.jiang@wwl.sg>
            Enviado: viernes, 31 de julio de 2026 19:21
            Asunto: UPDATE FAK WWL / CASTRO FALLAS /31-JULY
            Published FAK for your ref:
            POL
            POD
            CARRIER
            Free Time
            Validity (ETD)
            20'GP
            SHANGHAI
            Puerto Caldera
            PIL
            18 days dry
            7 Aug-14 Aug
            $6,700
            """;

        var payload = Convert.ToBase64String(Encoding.ASCII.GetBytes(currentBody));
        var rawEml = $"""
            From: Sonia Quiros <squiros@castrofallas.com>
            To: extraccioncastrofallas@gmail.com
            Subject: RV: UPDATE FAK WWL / CASTRO FALLAS / 12-AUG
            Date: Wed, 12 Aug 2026 14:38:56 +0000
            MIME-Version: 1.0
            Content-Type: multipart/alternative; boundary="wwl-test-boundary"

            --wwl-test-boundary
            Content-Type: text/plain; charset="gb2312"
            Content-Transfer-Encoding: base64

            {payload}
            --wwl-test-boundary--
            """;

        var extractor = new EmailDocumentExtractor();
        var document = await extractor.ExtractAsync(
            new DocumentExtractionInput(
                "RV_UPDATE_FAK_WWL_12-AUG.eml",
                "message/rfc822",
                ".eml",
                Encoding.ASCII.GetBytes(rawEml)
            )
        );

        var table = document.Tables.Single();
        var rows = table.Rows.ToArray();

        Assert.HasCount(4, rows);
        Assert.AreEqual("15 Aug", rows[0].Values["ValidFrom"]);
        Assert.AreEqual("21 Aug", rows[0].Values["ValidTo"]);
        Assert.AreEqual("14 Aug", rows[3].Values["ValidFrom"]);
        Assert.AreEqual("20 Aug", rows[3].Values["ValidTo"]);
        Assert.IsNotNull(document.RawText);
        Assert.IsFalse(document.RawText.Contains("$6,700", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ForwardedWwlFak_WhenAiOmitsDates_RecoversValidityFromNewestTableOnly()
    {
        const string body = """
            [cid:a890ddd2-246a-4f52-a9bd-13108fb8a556]
            Website : https://logisticacastrofallas.com
            AVISO LEGAL: Este mensaje es confidencial.
            ________________________________________________
            De: Veronica.jiang <veronica.jiang@wwl.sg>
            Enviado: miércoles, 12 de agosto de 2026 03:19
            Asunto: UPDATE FAK WWL / CASTRO FALLAS / 12-AUG

            Dear Royner,
            Published FAK for your ref:
            FAK
            POL
            POD
            CARRIER
            Free Time
            Validity (ETD)
            20'GP
            40'GP
            40'HQ
            SHA/NGB/SZN/XMN/TAO/TSN/DLN
            Acajulta/Corinto/Puerto Caldera
            MSC FAK
            21 days dry
            15 Aug-21 Aug
            $7,400
            $8,300
            $8,300
            SHA/NGB/SZN/XMN/TAO/TSN/DLN
            Acajulta/Corinto/Puerto Caldera
            MSC Basket
            21 days dry
            15 Aug-21 Aug
            $7,300
            $8,100
            $8,100
            SHA/NGB/SZN/XMN/TAO
            Acajulta/Corinto/Puerto Caldera
            ONE FAK
            16 days dry
            15 Aug-21 Aug
            $7,500
            $7,800
            $7,800
            SHANGHAI
            Acajulta/Corinto/Puerto Caldera
            PIL
            18 days dry
            14 Aug-20 Aug
            $7,700
            $7,900
            $7,900
            Sub to p/s $50/cntr & MBL RLS $75/BILL
            ________________________________________________
            Un saludo cordial
            Veronica Jiang
            De: Veronica.jiang <veronica.jiang@wwl.sg>
            Enviado: viernes, 31 de julio de 2026 19:21
            Asunto: UPDATE FAK WWL / CASTRO FALLAS /31-JULY
            Published FAK for your ref:
            FAK
            POL
            POD
            CARRIER
            Free Time
            Validity (ETD)
            20'GP
            40'GP
            40'HQ
            SHANGHAI
            Puerto Caldera
            PIL
            18 days dry
            7 Aug-14 Aug
            $6,700
            $6,900
            $6,900
            """;

        AiPricingEmailRow[] aiRows =
        [
            CreateRow("SHA", "Puerto Caldera", "MSC FAK", "20GP", 7400m),
            CreateRow("NGB", "Corinto", "MSC Basket", "40HQ", 8100m),
            CreateRow("TAO", "Acajulta", "ONE FAK", "40GP", 7800m),
            CreateRow("SHANGHAI", "Puerto Caldera", "PIL", "20GP", 7700m),
        ];

        var repaired = AutomatedPricingExtractionService
            .RepairMissingValidityFromEmailSource(
                aiRows,
                new AutomatedPricingExtractionContext(
                    Subject: "RV: UPDATE FAK WWL / CASTRO FALLAS / 12-AUG",
                    BodyText: body,
                    SourceType: "EmailBody"
                )
            )
            .ToArray();

        Assert.AreEqual(new DateTime(2026, 8, 15), repaired[0].ValidFrom);
        Assert.AreEqual(new DateTime(2026, 8, 21), repaired[0].ValidTo);
        Assert.AreEqual(new DateTime(2026, 8, 15), repaired[1].ValidFrom);
        Assert.AreEqual(new DateTime(2026, 8, 21), repaired[1].ValidTo);
        Assert.AreEqual(new DateTime(2026, 8, 15), repaired[2].ValidFrom);
        Assert.AreEqual(new DateTime(2026, 8, 21), repaired[2].ValidTo);
        Assert.AreEqual(new DateTime(2026, 8, 14), repaired[3].ValidFrom);
        Assert.AreEqual(new DateTime(2026, 8, 20), repaired[3].ValidTo);
        Assert.IsTrue(repaired.All(row =>
            row.Remarks?.Contains("Validity (ETD)", StringComparison.OrdinalIgnoreCase) == true
        ));
    }

    private static AiPricingEmailRow CreateRow(
        string pol,
        string poe,
        string carrier,
        string containerType,
        decimal freight
    )
    {
        return new AiPricingEmailRow(
            pol,
            poe,
            null,
            containerType,
            carrier,
            "WWL",
            null,
            "USD",
            null,
            null,
            null,
            null,
            freight,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null
        );
    }
}
