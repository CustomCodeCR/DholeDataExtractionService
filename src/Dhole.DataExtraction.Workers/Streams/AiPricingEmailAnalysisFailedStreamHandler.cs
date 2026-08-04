using CustomCodeFramework.Redis.Streams.Abstractions;
using CustomCodeFramework.Redis.Streams.Messages;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Contracts.AsyncEmail;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Persistence.DbContexts;
using Dhole.DataExtraction.Workers.Workers;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Workers.Streams;

internal sealed class AiPricingEmailAnalysisFailedStreamHandler(
    ServiceDbContext dbContext,
    ILogger<AiPricingEmailAnalysisFailedStreamHandler> logger
) : IRedisStreamMessageHandler
{
    public string MessageType => AsyncEmailMessageTypes.AiFailed;

    public async Task HandleAsync(
        RedisStreamEnvelope envelope,
        CancellationToken cancellationToken = default
    )
    {
        var integrationEvent =
            AsyncEmailStreamPayloadReader.Read<AiPricingEmailAnalysisFailedIntegrationEvent>(
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

        var isRecoveringLeaseFailure = job.CanRecoverAiLeaseFailure(
            integrationEvent.RequestId
        );
        var isAlreadyFinalized =
            job.Status
            is EmailExtractionJobStatus.AwaitingPricing
                or EmailExtractionJobStatus.SentToPricing
                or EmailExtractionJobStatus.Ignored;
        var isClosedWithoutRecoverableLeaseFailure =
            (
                job.Status
                is EmailExtractionJobStatus.NeedsReview
                    or EmailExtractionJobStatus.Failed
            )
            && !isRecoveringLeaseFailure;
        if (isAlreadyFinalized || isClosedWithoutRecoverableLeaseFailure)
        {
            return;
        }

        if (
            job.AiRequestId != integrationEvent.RequestId
            || !string.Equals(
                job.AiRequestHash,
                integrationEvent.RequestHash,
                StringComparison.Ordinal
            )
        )
        {
            logger.LogInformation(
                "Se ignoró un fallo AI obsoleto para el trabajo {EmailExtractionJobId}. "
                    + "Solicitud recibida {ReceivedRequestId}; solicitud activa {ActiveRequestId}.",
                job.Id,
                integrationEvent.RequestId,
                job.AiRequestId
            );
            return;
        }

        if (
            string.Equals(
                integrationEvent.ErrorCode,
                "AI.EmailJobLeaseExpired",
                StringComparison.Ordinal
            )
            && integrationEvent.IsTransient
        )
        {
            logger.LogWarning(
                "AI informó una pérdida recuperable de lease para el trabajo {EmailExtractionJobId}. "
                    + "La solicitud {AiRequestId} permanecerá activa y no se cerrará como fallida.",
                job.Id,
                integrationEvent.RequestId
            );
            return;
        }

        var request = await dbContext.EmailAiAnalysisRequests.FirstOrDefaultAsync(
            item => item.Id == integrationEvent.RequestId,
            cancellationToken
        ) ?? throw new InvalidOperationException(
            "No se encontró el payload de la solicitud AI fallida."
        );
        var payload =
            AsyncEmailStreamPayloadReader.ReadJson<AiPricingEmailAnalysisRequest>(
                request.PayloadJson
            );
        var hasDeterministicRows = payload.PreviousRows.Count > 0;
        var reason =
            "La normalización asistida por AI no pudo completarse: "
            + integrationEvent.ErrorMessage;

        if (hasDeterministicRows)
        {
            job.MarkNeedsReview(
                job.ExtractionExecutionId,
                payload.PreviousConfidence,
                reason,
                integrationEvent.ErrorCode
            );
        }
        else
        {
            job.MarkFailed(
                job.ExtractionExecutionId,
                integrationEvent.ErrorCode,
                reason
            );
        }

        request.MarkCompleted();
        await EmailJobStateCoordinator.RecalculateAsync(
            dbContext,
            job.EmailMessageId,
            cancellationToken
        );
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "AI agotó los intentos del trabajo {EmailExtractionJobId}. "
                + "Solicitud {AiRequestId}; AI job {AiJobId}; ejecución {AiExecutionId}; "
                + "intentos {AttemptCount}; código {ErrorCode}; estado {Status}.",
            job.Id,
            integrationEvent.RequestId,
            integrationEvent.AiJobId,
            integrationEvent.AiExecutionId,
            integrationEvent.AttemptCount,
            integrationEvent.ErrorCode,
            job.Status
        );
    }
}
