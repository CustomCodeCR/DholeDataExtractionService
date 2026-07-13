namespace Dhole.DataExtraction.Application.Abstractions.Emails;

public interface IEmailFileStorage
{
    Task<string> SaveRawEmailAsync(Guid emailMessageId, byte[] content, CancellationToken cancellationToken = default);

    Task<string> SaveAttachmentAsync(
        Guid emailMessageId,
        Guid attachmentId,
        string fileName,
        byte[] content,
        CancellationToken cancellationToken = default
    );

    Task<byte[]> ReadAsync(string storagePath, CancellationToken cancellationToken = default);
}
