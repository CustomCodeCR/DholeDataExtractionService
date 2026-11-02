using Dhole.DataExtraction.Domain.Emails;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class EmailTariffModalityPolicyTests
{
    [DataTestMethod]
    [DataRow("FCL Shanghai 40HC USD 2900")]
    [DataRow("LCL Miami consolidado CBM USD 65")]
    [DataRow("FTL Full truckload San José")]
    [DataRow("LTL road freight warehouse to San José")]
    [DataRow("AIR FREIGHT AWB rate USD 4.30/kg")]
    [DataRow("Flete aéreo de carga 1200 USD")]
    [DataRow("Carrier MSC POL Shanghai POD Balboa USD 2500")]
    [DataRow("1x40NOR POL Yantian POE Balboa")]
    public void RecognizesOnlySupportedFreightEvidence(string content)
    {
        Assert.IsTrue(EmailTariffModalityPolicy.HasSupportedModality(content), content);
        Assert.IsFalse(EmailTariffModalityPolicy.IsReadableAndUnrelated(content));
    }

    [DataTestMethod]
    [DataRow("Cotización de seguros corporativos y servicios contables para noviembre. No ofrecemos transporte ni fletes.")]
    [DataRow("Boletín informativo sobre cursos y capacitaciones de recursos humanos para empresas privadas.")]
    [DataRow("Factura por servicios de limpieza y mantenimiento general de oficinas durante el mes de octubre.")]
    public void ReadableNonFreightContent_IsInvalid(string content)
    {
        Assert.IsFalse(EmailTariffModalityPolicy.HasSupportedModality(content));
        Assert.IsTrue(EmailTariffModalityPolicy.IsReadableAndUnrelated(content));
    }

    [TestMethod]
    public void PackingListMentioningLclOrFcl_IsNotMistakenForARate()
    {
        var contents =
            "PACKING LIST - FCL 40HC - 28 CBM. Lista de mercancías y bultos " +
            "contenidos en el embarque, sin detalle de fletes ni precios.";
        Assert.IsTrue(EmailTariffModalityPolicy.HasSupportedModality(contents));
        Assert.IsTrue(EmailTariffModalityPolicy.IsReadableAndUnrelated(contents));
    }

    [TestMethod]
    public void UnreadableDocument_IsNotAutomaticallyDeclaredInvalid()
    {
        Assert.IsFalse(EmailTariffModalityPolicy.IsReadableAndUnrelated(""));
        Assert.IsFalse(EmailTariffModalityPolicy.IsReadableAndUnrelated("Image"));
    }

    [TestMethod]
    public void InvalidEmail_RemainsTerminalWithAnExplanatoryReason()
    {
        var message = EmailMessage.Create(
            Guid.NewGuid(), "msg-non-tariff", null, null, "Vendor",
            "vendor@example.com", "rates@example.com", null,
            "Capacitaciones", "Boletín informativo sin tarifas logísticas",
            null, DateTime.UtcNow, false, null, null);
        message.MarkInvalid(EmailTariffModalityPolicy.InvalidReason);
        Assert.AreEqual(EmailMessageStatus.Invalid, message.Status);
        StringAssert.Contains(message.ErrorMessage!, "Tarifa no válida");
    }
}
