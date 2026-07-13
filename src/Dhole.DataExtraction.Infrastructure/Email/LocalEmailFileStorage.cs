using Dhole.DataExtraction.Application.Abstractions.Emails;
using Microsoft.Extensions.Configuration;

namespace Dhole.DataExtraction.Infrastructure.Email;

public sealed class LocalEmailFileStorage(IConfiguration configuration) : IEmailFileStorage
{
    private readonly string _rootPath = ResolveRootPath(configuration);

    public async Task<string> SaveRawEmailAsync(Guid emailMessageId, byte[] content, CancellationToken cancellationToken = default)
    {
        var relativePath = Path.Combine("emails", emailMessageId.ToString("N"), "raw.eml");
        return await SaveAsync(relativePath, content, cancellationToken);
    }

    public async Task<string> SaveAttachmentAsync(
        Guid emailMessageId,
        Guid attachmentId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default
    )
    {
        var safeFileName = MakeSafeFileName(fileName);
        var relativePath = Path.Combine("emails", emailMessageId.ToString("N"), "attachments", $"{attachmentId:N}-{safeFileName}");
        return await SaveAsync(relativePath, content, cancellationToken);
    }

    public async Task<byte[]> ReadAsync(string storagePath, CancellationToken cancellationToken = default)
    {
        var path = Path.IsPathRooted(storagePath) ? storagePath : Path.Combine(_rootPath, storagePath);
        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    private async Task<string> SaveAsync(string relativePath, byte[] content, CancellationToken cancellationToken)
    {
        var path = Path.Combine(_rootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content, cancellationToken);
        return relativePath.Replace('\\', '/');
    }

    private static string ResolveRootPath(IConfiguration configuration)
    {
        var configured = configuration["EmailIngestion:StoragePath"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured.Trim());
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "storage", "data-extraction"));
    }

    private static string MakeSafeFileName(string fileName)
    {
        var value = string.IsNullOrWhiteSpace(fileName) ? "attachment.bin" : Path.GetFileName(fileName.Trim());
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value;
    }
}
