using System.Text;
using System.Text.Json;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Infrastructure.Extraction.Email;
using Dhole.DataExtraction.Infrastructure.Normalization;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class CarrierEmailPricingIntegrityRegressionTests
{
    [TestMethod]
    public async Task RslCalderaStackedMatrix_PreservesEverySourceRowAndEquipmentAmount()
    {
        const string body = """
            However, please note that rates for the Panama-Caribbean trade lane have not yet softened, and the rates for next week is pending.

            POL
            POD
            CARRIER
            20'
            40'/40HC
            Free time
            Effective Date
            Expiry date
            Shanghai/Ningbo/Qingdao
            Caldera
            OOCL
            $6,775
            $7,400
            18 days
            15-Sep
            21-Sep
            Xingang
            Caldera
            OOCL
            $6,825
            $7,450
            18 days
            15-Sep
            21-Sep
            Xiamen
            Caldera
            COSCO
            $6,815
            $7,415
            18 days
            15-Sep
            21-Sep
            Shanghai/Ningbo/Qingdao/Xingang
            Caldera
            PIL
            $6,900
            $7,400
            18 days
            15-Sep
            21-Sep

            General Cargo
            Subject to DTHC and local charges at both ends
            """;

        var extractor = new EmailDocumentExtractor();
        var document = await extractor.ExtractAsync(
            new DocumentExtractionInput(
                "rsl-caldera-current-rate.txt",
                "text/plain",
                ".txt",
                Encoding.UTF8.GetBytes(body)
            )
        );

        var table = document.Tables.Single();
        var rows = table.Rows.ToArray();

        Assert.HasCount(4, rows, "Cada fila comercial publicada debe sobrevivir a DataExtraction.");

        AssertSourceRow(rows[0], "Shanghai/Ningbo/Qingdao", "OOCL", "$6,775", "$7,400");
        AssertSourceRow(rows[1], "Xingang", "OOCL", "$6,825", "$7,450");
        AssertSourceRow(rows[2], "Xiamen", "COSCO", "$6,815", "$7,415");
        AssertSourceRow(rows[3], "Shanghai/Ningbo/Qingdao/Xingang", "PIL", "$6,900", "$7,400");

        Assert.IsTrue(rows.All(row => row.Values["POE"] == "Caldera"));
        Assert.IsTrue(rows.All(row => row.Values["ValidFrom"] == "15-Sep"));
        Assert.IsTrue(rows.All(row => row.Values["ValidTo"] == "21-Sep"));
        Assert.IsTrue(rows.All(row => row.Values["Free Time"] == "18 days"));
        Assert.IsTrue(rows.All(row => row.Values["Commodity"] == "General Cargo"));
    }

    [TestMethod]
    public void WwlNarrativeThread_UsesNewest7300RateAndNeverQuoted7500History()
    {
        const string body = """
            Dear Royner,

            Pls consider rate USD7300 per 40HC valid 15-21/Sep Carrier MSC/ONE NAC with 21 days free at dest, subject to space (except TIANJIN/XIAMEN)
            If big lot, case by case.
            BUT pls note space limited, first come first served.
            Subject to isps $15/cntr, p/s $50/cntr, MBL RLS at dest. $75/BL.

            Below the details of ONE NAC:
            Pls note, ONE NAC must match COMM as I listed below
            A)
            POL: Shanghai/Shekou/Qingdao/Ningbo
            POD: Acajutla/Corinto/Caldera
            COMM: Auto Spare Parts

            B)
            POL: Shanghai/Ningbo/Shekou/Yantian/Qingdao/Xiamen/Tianjin(+arb USD100)
            POD: Acajutla/Corinto/Caldera
            COMM: RETAIL

            C)
            POL: Shanghai/Yantian/Qingdao/Ningbo
            POD: Acajutla/Corinto/Caldera
            COMM: Solar Panels/Solar Modules/LED Lights

            From: Veronica.jiang <veronica.jiang@wwl.sg>
            Sent: 4 Sep 2026
            Subject: CASTRO FALLS// WWL CONTRACT ONE-MSC / SEP

            BELOW RATE ONLY AVAILABLE FOR NEW SHIPMENTS FROM TODAY, THE ONES ALREADY CONFIRMED KEEP UNCHANGED.
            Pls consider rate USD7500 per 40HC valid 8-14/Sep Carrier MSC/ONE NAC with 21 days free at dest, subject to space (except TIANJIN/XIAMEN)
            """;

        var table = EmailDocumentExtractor.TryExtractNarrativeNacTable(body);

        Assert.IsNotNull(table);
        Assert.IsGreaterThan(0, table.Rows.Count);
        Assert.IsTrue(table.Rows.All(row => row.Values["FreightAmount"] == "7300"));
        Assert.IsTrue(table.Rows.All(row => row.Values["ValidFrom"] == "15 Sep"));
        Assert.IsTrue(table.Rows.All(row => row.Values["ValidTo"] == "21 Sep"));
        Assert.IsFalse(table.Rows.Any(row => row.RawJson?.Contains("7500", StringComparison.Ordinal) == true));
    }

    [TestMethod]
    public void CommercialValidityDate_IsUtcMidnightWithoutCalendarDayShift()
    {
        var year = DateTime.UtcNow.Year;
        var from = DateNormalizer.Normalize("15-Sep");
        var to = DateNormalizer.Normalize("21-Sep");

        Assert.AreEqual(new DateTime(year, 9, 15, 0, 0, 0, DateTimeKind.Utc), from);
        Assert.AreEqual(new DateTime(year, 9, 21, 0, 0, 0, DateTimeKind.Utc), to);
        Assert.AreEqual(DateTimeKind.Utc, from!.Value.Kind);
        Assert.AreEqual(DateTimeKind.Utc, to!.Value.Kind);

        var json = JsonSerializer.Serialize(new { validFrom = from, validTo = to });
        Assert.Contains($"{year}-09-15T00:00:00Z", json, StringComparison.Ordinal);
        Assert.Contains($"{year}-09-21T00:00:00Z", json, StringComparison.Ordinal);
    }

    private static void AssertSourceRow(
        ExtractedRow row,
        string pol,
        string carrier,
        string twenty,
        string forty
    )
    {
        Assert.AreEqual(pol, row.Values["POL"]);
        Assert.AreEqual(carrier, row.Values["Carrier"]);
        Assert.AreEqual(twenty, row.Values["20GP"], "El monto 20' debe permanecer en su columna fuente.");
        Assert.AreEqual(forty, row.Values["40DV/40HC"], "El monto 40'/40HC debe permanecer en su columna fuente.");
    }
}
