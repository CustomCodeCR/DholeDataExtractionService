using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using ClosedXML.Excel;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Infrastructure.Email;
using Dhole.DataExtraction.Infrastructure.Files;
using ExcelDataReader;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Dhole.DataExtraction.Infrastructure.GrpcClients;

public sealed class AiEmailContentReader(
    IConfiguration configuration,
    ILogger<AiEmailContentReader> logger
) : IAiEmailContentReader
{
    private const int DefaultMaximumCharacters = 24_000;
    private const int MaximumWorksheetRows = 2_000;
    private const int MaximumWorksheetColumns = 100;
    private const double PdfRowTolerance = 3d;

    public Task<string> ReadAsTextAsync(
        string fileName,
        string? contentType,
        string? fileExtension,
        byte[] content,
        CancellationToken cancellationToken = default
    )
    {
        if (content.Length == 0)
        {
            return Task.FromResult(string.Empty);
        }

        var extension = NormalizeExtension(fileExtension, fileName);

        try
        {
            var text = extension switch
            {
                ".xlsx" or ".xlsm" => ReadExcel(content, cancellationToken),
                ".xls" => ReadLegacyExcel(content, cancellationToken),
                ".pdf" => ReadPdf(content, cancellationToken),
                ".docx" => ReadDocx(content, cancellationToken),
                ".rtf" => ReadRtf(content),
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".webp" or ".bmp" or ".tif" or ".tiff" =>
                    $"Imagen adjunta para análisis visual: {fileName}",
                ".html" or ".htm" => StripHtml(TextContentDecoder.Decode(content)),
                ".eml" => ReadEmail(content),
                ".txt" or ".csv" or ".json" or ".xml" or ".md" or ".tsv" or ".log" => TextContentDecoder.Decode(content),
                _ when IsTextContentType(contentType) => TextContentDecoder.Decode(content),
                _ => TryReadText(content),
            };

            // The filename is evidence. Carrier sheets frequently contain reused legal/footer
            // boilerplate from another line, while the attachment name identifies the actual
            // carrier and validity period. Keep that evidence adjacent to the extracted text.
            var enrichedText = $"## Archivo: {fileName}{Environment.NewLine}{text}";
            return Task.FromResult(Limit(TextContentDecoder.Clean(enrichedText)));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "No se pudo convertir el archivo {FileName} a texto para el fallback de IA.",
                fileName
            );

            return Task.FromResult(
                $"No fue posible convertir el contenido binario de '{fileName}' a texto. "
                + $"Tipo: {contentType ?? "desconocido"}. Error: {exception.Message}"
            );
        }
    }


    private static string ReadEmail(byte[] content)
    {
        try
        {
            var message = SimpleMimeParser.ParseRawMessage(content, "ai-email-content", null);
            return EmailPricingContentSelector.SelectPreferredBody(
                message.BodyText,
                message.BodyHtml
            );
        }
        catch
        {
            var fallback = SimpleMimeParser.ParseRawMessageFallback(
                content,
                "ai-email-content-fallback",
                null
            );
            return EmailPricingContentSelector.SelectPreferredBody(
                fallback.BodyText,
                fallback.BodyHtml
            );
        }
    }

    private string ReadExcel(byte[] content, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var workbook = new XLWorkbook(stream);
        var builder = new StringBuilder();

        foreach (var worksheet in workbook.Worksheets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var usedRange = worksheet.RangeUsed();
            if (usedRange is null)
            {
                continue;
            }

            builder.AppendLine($"## Hoja: {worksheet.Name}");

            var firstRow = usedRange.FirstRowUsed().RowNumber();
            var lastRow = Math.Min(
                usedRange.LastRowUsed().RowNumber(),
                firstRow + MaximumWorksheetRows - 1
            );
            var firstColumn = usedRange.FirstColumnUsed().ColumnNumber();
            var lastColumn = Math.Min(
                usedRange.LastColumnUsed().ColumnNumber(),
                firstColumn + MaximumWorksheetColumns - 1
            );

            for (var rowNumber = firstRow; rowNumber <= lastRow; rowNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var values = new string[lastColumn - firstColumn + 1];
                var hasValue = false;

                for (var columnNumber = firstColumn; columnNumber <= lastColumn; columnNumber++)
                {
                    var value = worksheet.Cell(rowNumber, columnNumber).GetFormattedString().Trim();
                    values[columnNumber - firstColumn] = value;
                    hasValue |= !string.IsNullOrWhiteSpace(value);
                }

                if (hasValue)
                {
                    builder.AppendLine(string.Join("\t", values.Select(EscapeTabValue)));
                }

                if (builder.Length >= MaximumCharacters)
                {
                    break;
                }
            }

            builder.AppendLine();

            if (builder.Length >= MaximumCharacters)
            {
                break;
            }
        }

        return builder.ToString();
    }

    private string ReadLegacyExcel(byte[] content, CancellationToken cancellationToken)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var stream = new MemoryStream(content, writable: false);
        using var reader = ExcelReaderFactory.CreateBinaryReader(stream);
        var builder = new StringBuilder();
        var worksheetCount = 0;

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            worksheetCount++;
            builder.AppendLine($"## Hoja: {reader.Name ?? $"Sheet{worksheetCount}"}");
            var rowCount = 0;

            while (reader.Read() && rowCount < MaximumWorksheetRows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var values = new List<string>(Math.Min(reader.FieldCount, MaximumWorksheetColumns));
                var hasValue = false;

                for (
                    var columnIndex = 0;
                    columnIndex < reader.FieldCount && columnIndex < MaximumWorksheetColumns;
                    columnIndex++
                )
                {
                    var value = reader.GetValue(columnIndex);
                    var text = value switch
                    {
                        null or DBNull => string.Empty,
                        DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                        _ => value.ToString() ?? string.Empty,
                    };
                    text = EscapeTabValue(text);
                    values.Add(text);
                    hasValue |= !string.IsNullOrWhiteSpace(text);
                }

                if (hasValue)
                {
                    builder.AppendLine(string.Join("\t", values));
                }

                rowCount++;
                if (builder.Length >= MaximumCharacters)
                {
                    break;
                }
            }

            builder.AppendLine();
            if (builder.Length >= MaximumCharacters)
            {
                break;
            }
        } while (reader.NextResult());

        return builder.ToString();
    }

    private string ReadDocx(byte[] content, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var builder = new StringBuilder();
        XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

        var entries = archive.Entries
            .Where(entry =>
                entry.FullName.Equals("word/document.xml", StringComparison.OrdinalIgnoreCase)
                || entry.FullName.StartsWith("word/header", StringComparison.OrdinalIgnoreCase)
                || entry.FullName.StartsWith("word/footer", StringComparison.OrdinalIgnoreCase)
            )
            .Where(entry => entry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.FullName)
            .ToArray();

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var entryStream = entry.Open();
            var document = XDocument.Load(entryStream, System.Xml.Linq.LoadOptions.PreserveWhitespace);

            foreach (var paragraph in document.Descendants(word + "p"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = string.Concat(paragraph.Descendants(word + "t").Select(node => node.Value));
                if (!string.IsNullOrWhiteSpace(text))
                {
                    builder.AppendLine(text.Trim());
                }

                if (builder.Length >= MaximumCharacters)
                {
                    break;
                }
            }

            if (builder.Length >= MaximumCharacters)
            {
                break;
            }
        }

        return builder.ToString();
    }

    private static string ReadRtf(byte[] content)
    {
        var rtf = TextContentDecoder.Decode(content);
        var decodedHex = Regex.Replace(
            rtf,
            @"\'(?<hex>[0-9a-fA-F]{2})",
            match => ((char)Convert.ToByte(match.Groups["hex"].Value, 16)).ToString()
        );
        var withoutDestinations = Regex.Replace(
            decodedHex,
            @"\\\*\s*\\[a-zA-Z]+-?\d* ?",
            string.Empty,
            RegexOptions.IgnoreCase
        );
        var withoutControls = Regex.Replace(
            withoutDestinations,
            @"\\[a-zA-Z]+-?\d* ?",
            " "
        );
        var withoutBraces = withoutControls.Replace('{', ' ').Replace('}', ' ');
        return Regex.Replace(withoutBraces, @"\s+", " ").Trim();
    }

    private string ReadPdf(byte[] content, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream(content, writable: false);
        using var document = PdfDocument.Open(stream);
        var builder = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            builder.AppendLine($"## Página {page.Number}");
            var reconstructedLines = ExtractPdfLines(page);
            if (reconstructedLines.Count > 0)
            {
                foreach (var line in reconstructedLines)
                {
                    builder.AppendLine(TextContentDecoder.Clean(line));
                }
            }
            else
            {
                builder.AppendLine(TextContentDecoder.Clean(page.Text));
            }
            builder.AppendLine();

            if (builder.Length >= MaximumCharacters)
            {
                break;
            }
        }

        return builder.ToString();
    }

    private static IReadOnlyCollection<string> ExtractPdfLines(Page page)
    {
        var words = page.GetWords()
            .Where(word => !string.IsNullOrWhiteSpace(word.Text))
            .OrderByDescending(word => word.BoundingBox.Bottom)
            .ThenBy(word => word.BoundingBox.Left)
            .ToArray();
        var lines = new List<string>();
        var currentRow = new List<Word>();
        double? currentY = null;

        foreach (var word in words)
        {
            var y = word.BoundingBox.Bottom;
            if (currentY is null || Math.Abs(currentY.Value - y) <= PdfRowTolerance)
            {
                currentRow.Add(word);
                currentY ??= y;
                continue;
            }

            AddRow();
            currentRow = [word];
            currentY = y;
        }

        AddRow();
        return lines;

        void AddRow()
        {
            if (currentRow.Count == 0)
            {
                return;
            }

            var orderedWords = currentRow
                .OrderBy(word => word.BoundingBox.Left)
                .ToArray();
            var row = new StringBuilder();
            Word? previous = null;

            foreach (var word in orderedWords)
            {
                if (previous is not null)
                {
                    var horizontalGap = word.BoundingBox.Left - previous.BoundingBox.Right;
                    row.Append(horizontalGap > 14d ? '\t' : ' ');
                }

                row.Append(word.Text.Trim());
                previous = word;
            }

            var value = TextContentDecoder.Clean(row.ToString()).Trim();
            if (!string.IsNullOrWhiteSpace(value))
            {
                lines.Add(value);
            }
        }
    }

    private string Limit(string value)
    {
        var maximumCharacters = int.TryParse(
            configuration["AI:EmailFallback:MaximumContentCharacters"],
            out var configured
        ) && configured > 0
            ? configured
            : DefaultMaximumCharacters;

        if (value.Length <= maximumCharacters)
        {
            return value;
        }

        const string marker = "\n[CONTENIDO INTERMEDIO OMITIDO POR LÍMITE]\n";
        var available = Math.Max(2, maximumCharacters - marker.Length);
        var headLength = available * 3 / 4;
        var tailLength = available - headLength;
        return value[..headLength] + marker + value[^tailLength..];
    }

    private static string StripHtml(string html)
    {
        var value = Regex.Replace(
            html,
            "<(script|style)[^>]*>.*?</\\1>",
            " ",
            RegexOptions.IgnoreCase | RegexOptions.Singleline
        );

        /* Conserva la estructura de las tablas pegadas desde Excel, Gmail u Outlook. */
        value = Regex.Replace(value, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, "</(td|th)>", "\t", RegexOptions.IgnoreCase);
        value = Regex.Replace(value, "</tr>", "\n", RegexOptions.IgnoreCase);
        value = Regex.Replace(
            value,
            "</(p|div|li|h[1-6])>",
            "\n",
            RegexOptions.IgnoreCase
        );
        value = Regex.Replace(value, "<[^>]+>", " ");

        var decoded = WebUtility.HtmlDecode(value)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var lines = decoded
            .Split('\n')
            .Select(line => Regex.Replace(line, @"[ ]+", " ").Trim(' ', '\t'))
            .Where(line => !string.IsNullOrWhiteSpace(line));

        return string.Join("\n", lines).Trim();
    }

    private static string TryReadText(byte[] content)
    {
        var text = TextContentDecoder.Decode(content);
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var controlCharacters = text.Count(character =>
            char.IsControl(character) && character is not '\r' and not '\n' and not '\t'
        );

        return controlCharacters > Math.Max(20, text.Length / 20)
            ? "El archivo es binario y no pudo representarse como texto legible."
            : text;
    }

    private static bool IsTextContentType(string? contentType)
    {
        return !string.IsNullOrWhiteSpace(contentType)
            && (
                contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("json", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("xml", StringComparison.OrdinalIgnoreCase)
            );
    }

    private static string NormalizeExtension(string? extension, string fileName)
    {
        var value = !string.IsNullOrWhiteSpace(extension)
            ? extension.Trim()
            : Path.GetExtension(fileName);

        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.StartsWith('.')
            ? value.ToLowerInvariant()
            : $".{value.ToLowerInvariant()}";
    }

    private static string EscapeTabValue(string value)
    {
        return value.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Trim();
    }

    private int MaximumCharacters =>
        int.TryParse(configuration["AI:EmailFallback:MaximumContentCharacters"], out var value)
        && value > 0
            ? value
            : DefaultMaximumCharacters;
}
