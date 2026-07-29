using System.Text;

namespace Dhole.DataExtraction.Infrastructure.Files;

/// <summary>
/// Decodes text attachments without assuming UTF-8 and repairs the most common
/// UTF-8/Windows-1252 mojibake produced by freight-rate documents.
/// </summary>
public static class TextContentDecoder
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    public static string Decode(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return Decode(content.AsSpan());
    }

    public static string Decode(ReadOnlySpan<byte> content)
    {
        if (content.IsEmpty)
        {
            return string.Empty;
        }

        string decoded;

        if (HasPrefix(content, 0xEF, 0xBB, 0xBF))
        {
            decoded = Encoding.UTF8.GetString(content[3..]);
        }
        else if (HasPrefix(content, 0xFF, 0xFE))
        {
            decoded = Encoding.Unicode.GetString(content[2..]);
        }
        else if (HasPrefix(content, 0xFE, 0xFF))
        {
            decoded = Encoding.BigEndianUnicode.GetString(content[2..]);
        }
        else
        {
            try
            {
                decoded = StrictUtf8.GetString(content);
            }
            catch (DecoderFallbackException)
            {
                decoded = DecodeWindows1252(content);
            }
        }

        return Clean(decoded);
    }

    public static string Clean(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var cleaned = value
            .Replace('\0', ' ')
            .Replace('\u00A0', ' ')
            .Replace("\uFEFF", string.Empty, StringComparison.Ordinal);

        cleaned = RepairUtf8Mojibake(cleaned);
        return cleaned.Normalize(NormalizationForm.FormC);
    }

    private static string RepairUtf8Mojibake(string value)
    {
        if (!LooksLikeUtf8Mojibake(value))
        {
            return value;
        }

        var bytes = EncodeWindows1252(value);
        string repaired;

        try
        {
            repaired = StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return value;
        }

        return SuspiciousScore(repaired) < SuspiciousScore(value) ? repaired : value;
    }

    private static bool LooksLikeUtf8Mojibake(string value)
    {
        return value.Contains('Ã')
            || value.Contains('Â')
            || value.Contains('Ð')
            || value.Contains('Ñ')
            || value.Contains("â€", StringComparison.Ordinal)
            || value.Contains("â€™", StringComparison.Ordinal)
            || value.Contains("â€œ", StringComparison.Ordinal)
            || value.Contains("â€�", StringComparison.Ordinal);
    }

    private static int SuspiciousScore(string value)
    {
        var score = 0;
        foreach (var character in value)
        {
            score += character switch
            {
                '\uFFFD' => 20,
                'Ã' or 'Â' => 4,
                '\0' => 10,
                _ when char.IsControl(character) && character is not '\r' and not '\n' and not '\t' => 3,
                _ => 0,
            };
        }

        return score;
    }

    private static string DecodeWindows1252(ReadOnlySpan<byte> content)
    {
        var builder = new StringBuilder(content.Length);

        foreach (var value in content)
        {
            builder.Append(value switch
            {
                0x80 => '\u20AC',
                0x82 => '\u201A',
                0x83 => '\u0192',
                0x84 => '\u201E',
                0x85 => '\u2026',
                0x86 => '\u2020',
                0x87 => '\u2021',
                0x88 => '\u02C6',
                0x89 => '\u2030',
                0x8A => '\u0160',
                0x8B => '\u2039',
                0x8C => '\u0152',
                0x8E => '\u017D',
                0x91 => '\u2018',
                0x92 => '\u2019',
                0x93 => '\u201C',
                0x94 => '\u201D',
                0x95 => '\u2022',
                0x96 => '\u2013',
                0x97 => '\u2014',
                0x98 => '\u02DC',
                0x99 => '\u2122',
                0x9A => '\u0161',
                0x9B => '\u203A',
                0x9C => '\u0153',
                0x9E => '\u017E',
                0x9F => '\u0178',
                _ => (char)value,
            });
        }

        return builder.ToString();
    }

    private static byte[] EncodeWindows1252(string value)
    {
        var bytes = new byte[value.Length];

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            bytes[index] = character switch
            {
                '\u20AC' => 0x80,
                '\u201A' => 0x82,
                '\u0192' => 0x83,
                '\u201E' => 0x84,
                '\u2026' => 0x85,
                '\u2020' => 0x86,
                '\u2021' => 0x87,
                '\u02C6' => 0x88,
                '\u2030' => 0x89,
                '\u0160' => 0x8A,
                '\u2039' => 0x8B,
                '\u0152' => 0x8C,
                '\u017D' => 0x8E,
                '\u2018' => 0x91,
                '\u2019' => 0x92,
                '\u201C' => 0x93,
                '\u201D' => 0x94,
                '\u2022' => 0x95,
                '\u2013' => 0x96,
                '\u2014' => 0x97,
                '\u02DC' => 0x98,
                '\u2122' => 0x99,
                '\u0161' => 0x9A,
                '\u203A' => 0x9B,
                '\u0153' => 0x9C,
                '\u017E' => 0x9E,
                '\u0178' => 0x9F,
                _ when character <= byte.MaxValue => (byte)character,
                _ => (byte)'?',
            };
        }

        return bytes;
    }

    private static bool HasPrefix(ReadOnlySpan<byte> content, params byte[] prefix)
    {
        return content.Length >= prefix.Length && content[..prefix.Length].SequenceEqual(prefix);
    }
}
