using Dhole.DataExtraction.Application.Abstractions.Files;
using Microsoft.Extensions.Configuration;

namespace Dhole.DataExtraction.Infrastructure.Files;

public sealed class LocalExtractionSourceFileStorage(IConfiguration configuration)
    : IExtractionSourceFileStorage
{
    public async Task<string> SaveAsync(
        Guid extractionExecutionId,
        string originalFileName,
        byte[] content,
        CancellationToken cancellationToken = default
    )
    {
        var configuredRoot = configuration["Storage:ExtractionFiles:RootPath"];
        var rootPath = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(AppContext.BaseDirectory, "storage", "pricing-imports")
            : Path.GetFullPath(configuredRoot);

        var executionDirectory = Path.Combine(rootPath, extractionExecutionId.ToString("N"));
        Directory.CreateDirectory(executionDirectory);

        var safeFileName = Path.GetFileName(originalFileName.Trim());
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            safeFileName = "source.bin";
        }

        var absolutePath = Path.Combine(executionDirectory, safeFileName);
        await File.WriteAllBytesAsync(absolutePath, content, cancellationToken);

        return absolutePath;
    }
}
