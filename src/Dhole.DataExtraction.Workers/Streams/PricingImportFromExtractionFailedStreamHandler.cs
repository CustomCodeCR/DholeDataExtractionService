using CustomCodeFramework.Redis.Streams.Abstractions;
using CustomCodeFramework.Redis.Streams.Messages;
using Dhole.DataExtraction.Contracts.AsyncEmail;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Persistence.DbContexts;
using Dhole.DataExtraction.Workers.Workers;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Workers.Streams;

internal sealed class PricingImportFromExtractionFailedStreamHandler(
    ServiceDbContext dbContext,
    ILogger<PricingImportFromExtractionFailedStreamHandler> logger
) : IRedisStreamMessageHandler
{
    public string MessageType => AsyncEmailMessageTypes.PricingFailed;

    public async Task HandleAsync(
        RedisStreamEnvelope envelope,
        CancellationToken cancellationToken = default
    )
    {
        var integrationEvent =
            AsyncEmailStreamPayloadReader.Read<PricingImportFromExtractionFailedIntegrationEvent>(
                envelope
            );
        var job = await dbContext.EmailExtractionJobs.FirstOrDefaultAsync(
            item =>
                item.Id == integrationEvent.EmailExtractionJobId
                && !item.IsDeleted,
            cancellationToken
        ) ?? throw new InvalidOperationException(
            "No se encontró el trabajo asociado al fallo de Pricing."
        );

        if (
            job.Status
            is EmailExtractionJobStatus.SentToPricing
                or EmailExtractionJobStatus.NeedsReview
        )
        {
            return;
        }

        if (job.PricingRequestId != integrationEvent.RequestId)
        {
            throw new InvalidOperationException(
                "El fallo de Pricing no corresponde a la solicitud activa."
            );
        }

        job.MarkNeedsReview(
            integrationEvent.ExtractionExecutionId,
            job.ConfidenceScore ?? 0m,
            $"Pricing no pudo persistir la importación: {integrationEvent.ErrorMessage}",
            integrationEvent.ErrorCode
        );
        await EmailJobStateCoordinator.RecalculateAsync(
            dbContext,
            job.EmailMessageId,
            cancellationToken
        );
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Pricing agotó los intentos del trabajo {EmailExtractionJobId}. "
                + "Solicitud {PricingRequestId}; código {ErrorCode}; intentos {AttemptCount}; "
                + "CorrelationId {CorrelationId}.",
            job.Id,
            integrationEvent.RequestId,
            integrationEvent.ErrorCode,
            integrationEvent.AttemptCount,
            integrationEvent.CorrelationId
        );
    }
}
