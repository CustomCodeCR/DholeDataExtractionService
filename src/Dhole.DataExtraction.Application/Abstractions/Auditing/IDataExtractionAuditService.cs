namespace Dhole.DataExtraction.Application.Abstractions.Auditing;

public interface IDataExtractionAuditService
{
    Task PublishAsync(
        DataExtractionAuditEvent auditEvent,
        CancellationToken cancellationToken = default
    );
}
