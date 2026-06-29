using Dhole.DataExtraction.Application.Abstractions.Files;
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
        if (string.IsNullOrWhiteSpace(originalFileName))
        {
            throw new InvalidOperationException(DataExtractionErrors.SourceDocumentFileNameRequired.Message);
        }

        if (fileContent.Length == 0)
        {
            throw new InvalidOperationException(DataExtractionErrors.EmptyFile.Message);
        }

        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        var fileHash = FileHashCalculator.ComputeSha256(fileContent);
        var sourceFileType = FileTypeDetector.Detect(originalFileName, contentType);

        if (sourceFileType == Dhole.DataExtraction.Domain.Extraction.Enums.SourceFileType.Unknown)
        {
            throw new InvalidOperationException(DataExtractionErrors.UnsupportedFileType.Message);
        }

        return Task.FromResult(
            new ExtractionFileInfo(
                originalFileName,
                contentType,
                extension,
                fileContent.LongLength,
                fileHash,
                sourceFileType,
                fileContent
            )
        );
    }
}
