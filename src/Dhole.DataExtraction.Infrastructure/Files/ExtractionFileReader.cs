using Dhole.DataExtraction.Application.Abstractions.Files;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using Dhole.DataExtraction.Domain.Shared;

namespace Dhole.DataExtraction.Infrastructure.Files;

public sealed class ExtractionFileReader : IExtractionFileReader
{
    public Task<ExtractionFileInfo> ReadAsync(
        string originalFileName,
        string? contentType,
        byte[] fileContent,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            throw new InvalidOperationException(DataExtractionErrors.SourceDocumentFileNameRequired.Message);
        }

        if (fileContent.Length == 0)
        {
            throw new InvalidOperationException(DataExtractionErrors.EmptyFile.Message);
        }

        var sourceFileType = FileTypeDetector.Detect(originalFileName, contentType, fileContent);

        if (sourceFileType == SourceFileType.Unknown)
        {
            throw new InvalidOperationException(DataExtractionErrors.UnsupportedFileType.Message);
        }

        var extension = ResolveExtension(originalFileName, sourceFileType);
        var normalizedFileName = NormalizeFileName(originalFileName, extension);
        var normalizedContentType = NormalizeContentType(contentType, sourceFileType);
        var fileHash = FileHashCalculator.ComputeSha256(fileContent);

        return Task.FromResult(
            new ExtractionFileInfo(
                normalizedFileName,
                normalizedContentType,
                extension,
                fileContent.LongLength,
                fileHash,
                sourceFileType,
                fileContent
            )
        );
    }

    private static string ResolveExtension(string originalFileName, SourceFileType sourceFileType)
    {
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();

        return string.IsNullOrWhiteSpace(extension)
            ? FileTypeDetector.GetDefaultExtension(sourceFileType)
            : extension;
    }

    private static string NormalizeFileName(string originalFileName, string extension)
    {
        var value = Path.GetFileName(originalFileName.Trim());

        if (string.IsNullOrWhiteSpace(Path.GetExtension(value)) && !string.IsNullOrWhiteSpace(extension))
        {
            value = $"{value}{extension}";
        }

        return value;
    }

    private static string? NormalizeContentType(string? contentType, SourceFileType sourceFileType)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            return contentType.Trim();
        }

        return sourceFileType switch
        {
            SourceFileType.Excel => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            SourceFileType.Csv => "text/csv",
            SourceFileType.Pdf => "application/pdf",
            SourceFileType.Email => "message/rfc822",
            _ => null,
        };
    }
}
