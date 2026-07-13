namespace Dhole.DataExtraction.Application.Abstractions.Files;

public interface IExtractionSourceFileStorage
{
    Task<string> SaveAsync(
        Guid extractionExecutionId,
        string originalFileName,
        byte[] content,
        CancellationToken cancellationToken = default
    );
}
