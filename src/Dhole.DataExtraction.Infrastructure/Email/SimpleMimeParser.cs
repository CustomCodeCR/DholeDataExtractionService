using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;
using Dhole.DataExtraction.Application.Abstractions.Emails;

namespace Dhole.DataExtraction.Infrastructure.Email;

internal static partial class SimpleMimeParser
{
    public static EmailMessageReadModel ParseRawMessage(byte[] rawContent, string externalMessageId, long? uid)
    {
        var rawText = DecodeText(rawContent);
        var (headers, body) = SplitHeadersAndBody(rawText);
        var headerMap = ParseHeaders(headers);
        var subject = DecodeHeader(GetHeader(headerMap, "Subject") ?? "(sin asunto)");
        var fromHeader = DecodeHeader(GetHeader(headerMap, "From") ?? string.Empty);
        var (fromName, fromAddress) = ParseMailbox(fromHeader);
        var receivedAt = ParseDate(GetHeader(headerMap, "Date")) ?? DateTime.UtcNow;

        var result = new MimeParseResult();
        ParsePart(headerMap, Encoding.UTF8.GetBytes(body), result);

        return new EmailMessageReadModel(
            externalMessageId,
            uid,
            NormalizeHeader(GetHeader(headerMap, "Message-ID")),
            fromName,
            string.IsNullOrWhiteSpace(fromAddress) ? "unknown@unknown.local" : fromAddress,
            DecodeHeader(GetHeader(headerMap, "To") ?? string.Empty),
            DecodeHeader(GetHeader(headerMap, "Cc") ?? string.Empty),
            subject,
            result.BodyText,
            result.BodyHtml,
            receivedAt,
            rawContent,
            result.Attachments
        );
    }

    private static void ParsePart(Dictionary<string, string> headers, byte[] bodyBytes, MimeParseResult result)
    {
        var contentType = GetHeader(headers, "Content-Type") ?? "text/plain";
        var transferEncoding = GetHeader(headers, "Content-Transfer-Encoding");
        var disposition = GetHeader(headers, "Content-Disposition");
        var boundary = GetParameter(contentType, "boundary");
        var fileName = DecodeHeader(GetParameter(disposition, "filename") ?? GetParameter(contentType, "name") ?? string.Empty);

        if (!string.IsNullOrWhiteSpace(boundary) && contentType.Contains("multipart/", StringComparison.OrdinalIgnoreCase))
        {
            var text = DecodeText(bodyBytes);
            foreach (var rawPart in SplitMultipart(text, boundary))
            {
                var (partHeadersText, partBodyText) = SplitHeadersAndBody(rawPart);
                var partHeaders = ParseHeaders(partHeadersText);
                ParsePart(partHeaders, Encoding.UTF8.GetBytes(partBodyText), result);
            }

            return;
        }

        var decodedBytes = DecodeTransfer(bodyBytes, transferEncoding);
        var isAttachment = !string.IsNullOrWhiteSpace(fileName)
            || (disposition?.Contains("attachment", StringComparison.OrdinalIgnoreCase) ?? false);

        if (isAttachment)
        {
            result.Attachments.Add(
                new EmailAttachmentReadModel(
                    string.IsNullOrWhiteSpace(fileName) ? $"attachment-{result.Attachments.Count + 1}.bin" : fileName,
                    NormalizeContentType(contentType),
                    decodedBytes
                )
            );

            return;
        }

        if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            result.BodyHtml = DecodeText(decodedBytes);
        }
        else if (contentType.Contains("text/plain", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(result.BodyText))
        {
            result.BodyText = DecodeText(decodedBytes);
        }
    }

    private static IEnumerable<string> SplitMultipart(string body, string boundary)
    {
        var marker = "--" + boundary;
        var endMarker = marker + "--";
        var segments = body.Split(marker, StringSplitOptions.None);

        foreach (var segment in segments.Skip(1))
        {
            if (segment.StartsWith("--", StringComparison.Ordinal))
            {
                yield break;
            }

            var cleaned = segment.TrimStart('\r', '\n').TrimEnd('\r', '\n');
            if (!string.IsNullOrWhiteSpace(cleaned) && !cleaned.StartsWith(endMarker, StringComparison.Ordinal))
            {
                yield return cleaned;
            }
        }
    }

    private static byte[] DecodeTransfer(byte[] content, string? transferEncoding)
    {
        var encoding = transferEncoding?.Trim().ToLowerInvariant();
        var text = DecodeText(content).Trim();

        try
        {
            return encoding switch
            {
                "base64" => Convert.FromBase64String(RemoveWhitespace(text)),
                "quoted-printable" => DecodeQuotedPrintable(text),
                _ => content,
            };
        }
        catch
        {
            return content;
        }
    }

    private static byte[] DecodeQuotedPrintable(string input)
    {
        input = input.Replace("=\r\n", string.Empty).Replace("=\n", string.Empty);
        using var output = new MemoryStream();

        for (var i = 0; i < input.Length; i++)
        {
            if (input[i] == '=' && i + 2 < input.Length && IsHex(input[i + 1]) && IsHex(input[i + 2]))
            {
                var hex = input.Substring(i + 1, 2);
                output.WriteByte(Convert.ToByte(hex, 16));
                i += 2;
            }
            else
            {
                var bytes = Encoding.UTF8.GetBytes(input[i].ToString());
                output.Write(bytes, 0, bytes.Length);
            }
        }

        return output.ToArray();
    }

    private static bool IsHex(char value) => char.IsAsciiHexDigit(value);

    private static string RemoveWhitespace(string value)
    {
        return Regex.Replace(value, "\\s+", string.Empty);
    }

    private static string DecodeText(byte[] content)
    {
        try
        {
            return Encoding.UTF8.GetString(content);
        }
        catch
        {
            return Encoding.Latin1.GetString(content);
        }
    }

    private static (string Headers, string Body) SplitHeadersAndBody(string raw)
    {
        var index = raw.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        var separatorLength = 4;

        if (index < 0)
        {
            index = raw.IndexOf("\n\n", StringComparison.Ordinal);
            separatorLength = 2;
        }

        return index < 0
            ? (raw, string.Empty)
            : (raw[..index], raw[(index + separatorLength)..]);
    }

    private static Dictionary<string, string> ParseHeaders(string headers)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentKey = null;

        foreach (var rawLine in headers.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (string.IsNullOrEmpty(line))
            {
                continue;
            }

            if ((line.StartsWith(' ') || line.StartsWith('\t')) && currentKey is not null)
            {
                result[currentKey] = result[currentKey] + " " + line.Trim();
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                continue;
            }

            currentKey = line[..separatorIndex].Trim();
            result[currentKey] = line[(separatorIndex + 1)..].Trim();
        }

        return result;
    }

    private static string? GetHeader(Dictionary<string, string> headers, string name)
    {
        return headers.TryGetValue(name, out var value) ? value : null;
    }

    private static string? GetParameter(string? header, string name)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return null;
        }

        var match = Regex.Match(
            header,
            $"(?:^|;)\\s*{Regex.Escape(name)}\\*?=\\s*(?:\"(?<q>[^\"]*)\"|(?<v>[^;]+))",
            RegexOptions.IgnoreCase
        );

        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["q"].Success ? match.Groups["q"].Value : match.Groups["v"].Value;
        return value.Trim();
    }

    private static string DecodeHeader(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return EncodedWordRegex().Replace(value, match =>
        {
            var charset = match.Groups["charset"].Value;
            var encoding = match.Groups["encoding"].Value.ToUpperInvariant();
            var data = match.Groups["data"].Value;

            try
            {
                var bytes = encoding == "B"
                    ? Convert.FromBase64String(data)
                    : DecodeQuotedPrintable(data.Replace('_', ' '));

                var textEncoding = Encoding.GetEncoding(charset);
                return textEncoding.GetString(bytes);
            }
            catch
            {
                return match.Value;
            }
        }).Trim();
    }

    private static (string? Name, string? Address) ParseMailbox(string fromHeader)
    {
        if (string.IsNullOrWhiteSpace(fromHeader))
        {
            return (null, null);
        }

        try
        {
            var address = new MailAddress(fromHeader);
            return (string.IsNullOrWhiteSpace(address.DisplayName) ? null : address.DisplayName, address.Address);
        }
        catch
        {
            var match = Regex.Match(fromHeader, "(?<mail>[A-Z0-9._%+-]+@[A-Z0-9.-]+\\.[A-Z]{2,})", RegexOptions.IgnoreCase);
            return (null, match.Success ? match.Groups["mail"].Value : fromHeader.Trim());
        }
    }

    private static DateTime? ParseDate(string? value)
    {
        return DateTimeOffset.TryParse(value, out var parsed) ? parsed.UtcDateTime : null;
    }

    private static string? NormalizeHeader(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : DecodeHeader(value).Trim('<', '>', ' ');
    }

    private static string? NormalizeContentType(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var separator = value.IndexOf(';');
        return separator < 0 ? value.Trim() : value[..separator].Trim();
    }

    private sealed class MimeParseResult
    {
        public string? BodyText { get; set; }
        public string? BodyHtml { get; set; }
        public List<EmailAttachmentReadModel> Attachments { get; } = [];
    }

    [GeneratedRegex("=\\?(?<charset>[^?]+)\\?(?<encoding>[bBqQ])\\?(?<data>[^?]*)\\?=")]
    private static partial Regex EncodedWordRegex();
}
