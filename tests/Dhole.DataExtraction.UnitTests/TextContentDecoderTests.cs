using System.Text;
using Dhole.DataExtraction.Infrastructure.Files;

namespace Dhole.DataExtraction.UnitTests;

[TestClass]
public sealed class TextContentDecoderTests
{
    [TestMethod]
    public void Decode_UsesWindows1252WhenBytesAreNotValidUtf8()
    {
        byte[] content = [0x4D, 0x6F, 0xED, 0x6E, 0x20, 0x43, 0x6F, 0x72, 0x74, 0xE9, 0x73];

        var value = TextContentDecoder.Decode(content);

        Assert.AreEqual("Moín Cortés", value);
    }

    [TestMethod]
    public void Clean_RepairsCommonUtf8Mojibake()
    {
        var value = TextContentDecoder.Clean("MoÃ­n / Puerto CortÃ©s");

        Assert.AreEqual("Moín / Puerto Cortés", value);
    }
}
