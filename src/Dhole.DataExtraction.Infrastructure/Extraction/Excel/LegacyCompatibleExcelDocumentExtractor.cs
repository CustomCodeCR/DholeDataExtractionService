using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using ExcelDataReader;

namespace Dhole.DataExtraction.Infrastructure.Extraction.Excel;

/// <summary>
/// Keeps the existing ClosedXML extraction pipeline for OpenXML workbooks and
/// transparently converts legacy BIFF .xls files before delegating to it.
/// </summary>
public sealed class LegacyCompatibleExcelDocumentExtractor(
    ExcelDocumentExtractor innerExtractor
) : IDocumentExtractor
{
    private static readonly byte[] CompoundFileSignature =
        [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    public SourceFileType FileType => SourceFileType.Excel;

    public Task<ExtractedDocument> ExtractAsync(
        DocumentExtractionInput input,
        CancellationToken cancellationToken = default
    )
    {
        if (!IsLegacyWorkbook(input))
        {
            return innerExtractor.ExtractAsync(input, cancellationToken);
        }

        var convertedContent = ConvertLegacyWorkbook(input.FileContent, cancellationToken);
        var convertedInput = input with
        {
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            FileExtension = ".xlsx",
            FileContent = convertedContent,
        };

        return innerExtractor.ExtractAsync(convertedInput, cancellationToken);
    }

    private static bool IsLegacyWorkbook(DocumentExtractionInput input)
    {
        if (string.Equals(input.FileExtension, ".xls", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return input.FileContent.AsSpan().StartsWith(CompoundFileSignature);
    }

    private static byte[] ConvertLegacyWorkbook(
        byte[] content,
        CancellationToken cancellationToken
    )
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        using var source = new MemoryStream(content, writable: false);
        using var reader = ExcelReaderFactory.CreateBinaryReader(source);
        using var workbook = new XLWorkbook();
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sheetIndex = 1;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sheetName = BuildUniqueWorksheetName(reader.Name, sheetIndex++, usedNames);
            var worksheet = workbook.Worksheets.Add(sheetName);
            var rowNumber = 1;

            while (reader.Read())
            {
                cancellationToken.ThrowIfCancellationRequested();

                for (var columnIndex = 0; columnIndex < reader.FieldCount; columnIndex++)
                {
                    var value = reader.GetValue(columnIndex);
                    if (value is null || value is DBNull)
                    {
                        continue;
                    }

                    var cell = worksheet.Cell(rowNumber, columnIndex + 1);
                    switch (value)
                    {
                        case DateTime date:
                            cell.Value = date;
                            break;
                        case bool boolean:
                            cell.Value = boolean;
                            break;
                        case double number:
                            cell.Value = number;
                            break;
                        case float number:
                            cell.Value = number;
                            break;
                        case decimal number:
                            cell.Value = Convert.ToDouble(number, CultureInfo.InvariantCulture);
                            break;
                        case int number:
                            cell.Value = number;
                            break;
                        case long number:
                            cell.Value = number;
                            break;
                        default:
                            cell.Value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                            break;
                    }
                }

                rowNumber++;
            }
        } while (reader.NextResult());

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    private static string BuildUniqueWorksheetName(
        string? requestedName,
        int sheetIndex,
        ISet<string> usedNames
    )
    {
        var baseName = string.IsNullOrWhiteSpace(requestedName)
            ? $"Sheet{sheetIndex}"
            : requestedName.Trim();
        foreach (var invalid in new[] { ':', '\\', '/', '?', '*', '[', ']' })
        {
            baseName = baseName.Replace(invalid, '-');
        }

        if (baseName.Length > 31)
        {
            baseName = baseName[..31];
        }

        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = $"Sheet{sheetIndex}";
        }

        var candidate = baseName;
        var suffix = 2;
        while (!usedNames.Add(candidate))
        {
            var marker = $"-{suffix++}";
            var maximumBaseLength = Math.Max(1, 31 - marker.Length);
            candidate = baseName[..Math.Min(baseName.Length, maximumBaseLength)] + marker;
        }

        return candidate;
    }
}
