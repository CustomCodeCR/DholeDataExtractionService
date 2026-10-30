using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Extraction.Enums;

namespace Dhole.DataExtraction.Domain.Emails;

/// <summary>
/// One shared attachment allowlist for polling, recovery and extraction.
/// Unknown file types are accepted only for known document formats converted
/// by MarkItDown. Executables, scripts and arbitrary binaries remain excluded.
/// </summary>
public static class EmailAttachmentExtractionPolicy
{
    public const string SupportedTypesDescription =
        "PDF, Excel, CSV/TSV, imágenes, HTML, TXT/Markdown, Word, RTF, PowerPoint, EML/MSG, JSON/XML y ZIP";

    public static bool IsSupported(EmailAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        return IsSupported(attachment.SourceFileType, attachment.FileExtension);
    }

    public static bool RequiresMarkdownConversion(string? extension) =>
        NormalizeExtension(extension) is
            ".doc" or ".docx" or ".rtf" or ".pptx" or ".msg" or ".zip";

    public static bool IsSupported(SourceFileType sourceFileType, string? fileExtension)
    {
        return NormalizeExtension(fileExtension) switch
        {
            ".pdf" => sourceFileType == SourceFileType.Pdf,
            ".csv" => sourceFileType == SourceFileType.Csv,
            ".xlsx" or ".xlsm" or ".xls" => sourceFileType == SourceFileType.Excel,
            ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp"
                or ".tif" or ".tiff" => sourceFileType == SourceFileType.Image,
            ".html" or ".htm" or ".txt" or ".md" or ".tsv" or ".json" or ".xml"
                or ".log" or ".eml" or ".mht" or ".mhtml" or ".mail" =>
                sourceFileType == SourceFileType.Email,
            ".doc" => sourceFileType is SourceFileType.Unknown or SourceFileType.Excel,
            ".rtf" => sourceFileType is SourceFileType.Unknown or SourceFileType.Email,
            ".docx" or ".pptx" or ".zip" =>
                sourceFileType == SourceFileType.Unknown,
            ".msg" => sourceFileType is SourceFileType.Email or SourceFileType.Unknown,
            _ => false
        };
    }

    private static string NormalizeExtension(string? extension) =>
        string.IsNullOrWhiteSpace(extension)
            ? string.Empty
            : extension.Trim().StartsWith('.')
                ? extension.Trim().ToLowerInvariant()
                : "." + extension.Trim().ToLowerInvariant();
}
