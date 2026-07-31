using CustomCodeFramework.Redis.Streams.Abstractions;
using CustomCodeFramework.Redis.Streams.Messages;
using Dhole.DataExtraction.Contracts.AsyncEmail;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Workers.Streams;

internal sealed class AiPricingEmailAnalysisStartedStreamHandler(
    ServiceDbContext dbContext,
    ILogger<AiPricingEmailAnalysisStartedStreamHandler> logger
) : IRedisStreamMessageHandler
{
    public string MessageType => AsyncEmailMessageTypes.AiStarted;

    public async Task HandleAsync(
        RedisStreamEnvelope envelope,
        CancellationToken cancellationToken = default
    )
    {
        var integrationEvent =
            AsyncEmailStreamPayloadReader.Read<AiPricingEmailAnalysisStartedIntegrationEvent>(
                envelope
            );
        var job = await dbContext.EmailExtractionJobs.FirstOrDefaultAsync(
            item =>
                item.Id == integrationEvent.EmailExtractionJobId
                && !item.IsDeleted,
            cancellationToken
        ) ?? throw new InvalidOperationException(
            "No se encontró el trabajo de correo informado por AI."
        );

        if (job.AiRequestId != integrationEvent.RequestId)
        {
            logger.LogInformation(
                "Se ignoró un evento AI started obsoleto para el trabajo {EmailExtractionJobId}. "
                    + "Solicitud recibida {ReceivedRequestId}; solicitud activa {ActiveRequestId}.",
                job.Id,
                integrationEvent.RequestId,
                job.AiRequestId
            );
            return;
        }

        job.MarkAiProcessing(integrationEvent.RequestId);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "AI inició el trabajo {EmailExtractionJobId}. Solicitud {AiRequestId}; "
                + "AI job {AiJobId}; CorrelationId {CorrelationId}.",
            job.Id,
            integrationEvent.RequestId,
            integrationEvent.AiJobId,
            integrationEvent.CorrelationId
        );
    }
}
