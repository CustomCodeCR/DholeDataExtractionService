using System.IO.Compression;
using System.Text;
using Dhole.DataExtraction.Domain.Extraction.Enums;

namespace Dhole.DataExtraction.Infrastructure.Files;

public static class FileTypeDetector
{
    public static SourceFileType Detect(string? fileName, string? contentType, byte[]? content = null)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        var type = contentType?.ToLowerInvariant();

        var extensionResult = DetectByExtension(extension);
        if (extensionResult != SourceFileType.Unknown)
        {
            return extensionResult;
        }

        var contentTypeResult = DetectByContentType(type);
        if (contentTypeResult != SourceFileType.Unknown)
        {
            return contentTypeResult;
        }

        return DetectByContent(content);
    }

    public static string GetDefaultExtension(SourceFileType sourceFileType)
    {
        return sourceFileType switch
        {
            SourceFileType.Excel => ".xlsx",
            SourceFileType.Csv => ".csv",
            SourceFileType.Pdf => ".pdf",
            SourceFileType.Email => ".eml",
            _ => string.Empty,
        };
    }

    private static SourceFileType DetectByExtension(string extension)
    {
        return extension switch
        {
            ".xlsx" or ".xlsm" or ".xls" => SourceFileType.Excel,
            ".csv" => SourceFileType.Csv,
            ".pdf" => SourceFileType.Pdf,
            ".eml" or ".msg" or ".html" or ".htm" or ".txt" or ".mail" or ".mht" or ".mhtml" => SourceFileType.Email,
            _ => SourceFileType.Unknown,
        };
    }

    private static SourceFileType DetectByContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return SourceFileType.Unknown;
        }

        if (contentType.Contains("spreadsheet")
            || contentType.Contains("excel")
            || contentType.Contains("vnd.ms-excel")
            || contentType.Contains("officedocument.spreadsheetml"))
        {
            return SourceFileType.Excel;
        }

        if (contentType.Contains("csv"))
        {
            return SourceFileType.Csv;
        }

        if (contentType.Contains("pdf"))
        {
            return SourceFileType.Pdf;
        }

        if (contentType.Contains("message/rfc822")
            || contentType.Contains("text/html")
            || contentType.Contains("text/plain")
            || contentType.Contains("multipart/"))
        {
            return SourceFileType.Email;
        }

        return SourceFileType.Unknown;
    }

    private static SourceFileType DetectByContent(byte[]? content)
    {
        if (content is null || content.Length == 0)
        {
            return SourceFileType.Unknown;
        }

        if (content.Length >= 5 && Encoding.ASCII.GetString(content, 0, 5) == "%PDF-")
        {
            return SourceFileType.Pdf;
        }

        if (LooksLikeOleExcel(content) || LooksLikeOpenXmlSpreadsheet(content))
        {
            return SourceFileType.Excel;
        }

        if (LooksLikeCsv(content))
        {
            return SourceFileType.Csv;
        }

        return LooksLikeEmailOrPlainText(content) ? SourceFileType.Email : SourceFileType.Unknown;
    }

    private static bool LooksLikeOleExcel(byte[] content)
    {
        return content.Length >= 8
            && content[0] == 0xD0
            && content[1] == 0xCF
            && content[2] == 0x11
            && content[3] == 0xE0
            && content[4] == 0xA1
            && content[5] == 0xB1
            && content[6] == 0x1A
            && content[7] == 0xE1;
    }

    private static bool LooksLikeOpenXmlSpreadsheet(byte[] content)
    {
        if (content.Length < 4 || content[0] != 0x50 || content[1] != 0x4B)
        {
            return false;
        }

        try
        {
            using var stream = new MemoryStream(content, writable: false);
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

            return archive.Entries.Any(entry =>
                entry.FullName.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.FullName, "[Content_Types].xml", StringComparison.OrdinalIgnoreCase)
                    && EntryContains(entry, "spreadsheetml")
            );
        }
        catch
        {
            return false;
        }
    }

    private static bool EntryContains(ZipArchiveEntry entry, string value)
    {
        try
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
            var text = reader.ReadToEnd();
            return text.Contains(value, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeCsv(byte[] content)
    {
        var sampleLength = Math.Min(content.Length, 8192);
        var sample = Encoding.UTF8.GetString(content, 0, sampleLength);

        if (sample.Any(char.IsControl) && !sample.Any(ch => ch is '\r' or '\n' or '\t'))
        {
            return false;
        }

        var lines = sample
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(5)
            .ToArray();

        return lines.Length > 0
            && lines.Any(line => line.Contains(',') || line.Contains(';') || line.Contains('|') || line.Contains('\t'));
    }

    private static bool LooksLikeEmailOrPlainText(byte[] content)
    {
        var sampleLength = Math.Min(content.Length, 8192);
        var sample = Encoding.UTF8.GetString(content, 0, sampleLength);

        if (sample.Any(char.IsControl) && !sample.Any(ch => ch is '\r' or '\n' or '\t'))
        {
            return false;
        }

        return sample.Contains("Subject:", StringComparison.OrdinalIgnoreCase)
            || sample.Contains("From:", StringComparison.OrdinalIgnoreCase)
            || sample.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || sample.Contains("<table", StringComparison.OrdinalIgnoreCase)
            || sample.Contains("POL", StringComparison.OrdinalIgnoreCase)
            || sample.Contains("POD", StringComparison.OrdinalIgnoreCase)
            || sample.Contains("Ocean Freight", StringComparison.OrdinalIgnoreCase)
            || sample.Contains("Flete", StringComparison.OrdinalIgnoreCase);
    }

}

