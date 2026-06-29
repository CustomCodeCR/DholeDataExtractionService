using Dhole.DataExtraction.Domain.Extraction.Enums;

namespace Dhole.DataExtraction.Infrastructure.Files;

public static class FileTypeDetector
{
    public static SourceFileType Detect(string? fileName, string? contentType)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        var type = contentType?.ToLowerInvariant();

        if (extension is ".xlsx" or ".xlsm" or ".xls")
        {
            return SourceFileType.Excel;
        }

        if (extension is ".csv")
        {
            return SourceFileType.Csv;
        }

        if (extension is ".pdf")
        {
            return SourceFileType.Pdf;
        }

        if (type is not null)
        {
            if (type.Contains("spreadsheet") || type.Contains("excel"))
            {
                return SourceFileType.Excel;
            }

            if (type.Contains("csv"))
            {
                return SourceFileType.Csv;
            }

            if (type.Contains("pdf"))
            {
                return SourceFileType.Pdf;
            }
        }

        return SourceFileType.Unknown;
    }
}
