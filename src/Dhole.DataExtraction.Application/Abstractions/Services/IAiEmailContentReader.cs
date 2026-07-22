namespace Dhole.DataExtraction.Application.Abstractions.Services;

public interface IAiEmailContentReader
{
    Task<string> ReadAsTextAsync(
        string fileName,
        string? contentType,
        string? fileExtension,
        byte[] content,
        CancellationToken cancellationToken = default
    );
}
