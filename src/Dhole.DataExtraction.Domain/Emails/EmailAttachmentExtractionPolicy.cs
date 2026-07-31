using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Extraction.Enums;

namespace Dhole.DataExtraction.Domain.Emails;

public static class EmailAttachmentExtractionPolicy
{
    public const string SupportedTypesDescription = "PDF, CSV o XLSX";

    public static bool IsSupported(EmailAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        return IsSupported(
            attachment.SourceFileType,
            attachment.FileExtension
        );
    }

    public static bool IsSupported(
        SourceFileType sourceFileType,
        string? fileExtension
    )
    {
        var extension = NormalizeExtension(fileExtension);

        return extension switch
        {
            ".pdf" => sourceFileType == SourceFileType.Pdf,
            ".csv" => sourceFileType == SourceFileType.Csv,
            ".xlsx" => sourceFileType == SourceFileType.Excel,
            _ => false,
        };
    }

    private static string NormalizeExtension(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var extension = value.Trim().ToLowerInvariant();
        return extension.StartsWith('.') ? extension : $".{extension}";
    }
}
