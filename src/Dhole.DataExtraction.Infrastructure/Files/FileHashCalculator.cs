using System.Security.Cryptography;

namespace Dhole.DataExtraction.Infrastructure.Files;

public static class FileHashCalculator
{
    public static string ComputeSha256(byte[] content)
    {
        if (content.Length == 0)
        {
            return string.Empty;
        }

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(content);

        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
