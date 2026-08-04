using System.Data;
using CustomCodeFramework.Redis.Streams.Abstractions;
using CustomCodeFramework.Redis.Streams.Messages;
using Dhole.DataExtraction.Application.Abstractions.Emails;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Application.Abstractions.Messaging;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Contracts.AsyncEmail;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Persistence.DbContexts;
using Dhole.DataExtraction.Workers.Workers;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Workers.Streams;

internal sealed class AiPricingEmailAnalysisCompletedStreamHandler(
    ServiceDbContext dbContext,
    IAutomatedPricingExtractionService automatedExtraction,
    IEmailRateClassifier classifier,
    IIntegrationEventOutboxWriter outbox,
    ILogger<AiPricingEmailAnalysisCompletedStreamHandler> logger
) : IRedisStreamMessageHandler
{
    private static readonly HashSet<string> ReviewablePricingIssueCodes = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "missing_agent",
        "unknown_agent",
        "missing_destination_port",
        "same_poe_and_pod",
        "missing_currency",
    };

    public string MessageType => AsyncEmailMessageTypes.AiCompleted;

    public async Task HandleAsync(
        RedisStreamEnvelope envelope,
        CancellationToken cancellationToken = default
    )
    {
        var integrationEvent =
            AsyncEmailStreamPayloadReader.Read<AiPricingEmailAnalysisCompletedIntegrationEvent>(
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

        var isRecoveredLeaseResult = job.CanRecoverAiLeaseFailure(
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
            && !isRecoveredLeaseResult;
        if (isAlreadyFinalized || isClosedWithoutRecoverableLeaseFailure)
        {
            return;
        }

        if (!IsActiveRequest(job.AiRequestId, job.AiRequestHash, integrationEvent))
        {
            logger.LogInformation(
                "Se ignoró un resultado AI obsoleto para el trabajo {EmailExtractionJobId}. "
                    + "Solicitud recibida {ReceivedRequestId}; solicitud activa {ActiveRequestId}.",
                job.Id,
                integrationEvent.RequestId,
                job.AiRequestId
            );
            return;
        }

        var aiRequest = await dbContext.EmailAiAnalysisRequests.FirstOrDefaultAsync(
            item => item.Id == integrationEvent.RequestId,
            cancellationToken
        ) ?? throw new InvalidOperationException(
            "No se encontró el payload preparado para la respuesta de AI."
        );
        if (aiRequest.CompletedAtUtc.HasValue && !isRecoveredLeaseResult)
        {
            return;
        }

        var payload =
            AsyncEmailStreamPayloadReader.ReadJson<AiPricingEmailAnalysisRequest>(
                aiRequest.PayloadJson
            );
        var message = await dbContext.EmailMessages.FirstOrDefaultAsync(
            item => item.Id == job.EmailMessageId && !item.IsDeleted,
            cancellationToken
        ) ?? throw new InvalidOperationException(
            "No se encontró el correo asociado al resultado de AI."
        );
        var account = await dbContext.EmailIngestionAccounts.FirstOrDefaultAsync(
            item =>
                item.Id == message.EmailIngestionAccountId && !item.IsDeleted,
            cancellationToken
        ) ?? throw new InvalidOperationException(
            "No se encontró la cuenta asociada al resultado de AI."
        );
        var attachment = job.EmailAttachmentId.HasValue
            ? await dbContext.EmailAttachments.FirstOrDefaultAsync(
                item =>
                    item.Id == job.EmailAttachmentId.Value && !item.IsDeleted,
                cancellationToken
            )
            : null;

        if (isRecoveredLeaseResult)
        {
            aiRequest.ReopenAfterLeaseRecovery();
            job.MarkValidatingRecoveredAiResult(
                integrationEvent.RequestId,
                integrationEvent.AiExecutionId
            );
        }
        else
        {
            job.MarkValidatingAiResult(
                integrationEvent.RequestId,
                integrationEvent.AiExecutionId
            );
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        var analysis = new AiPricingEmailAnalysisResult(
            true,
            integrationEvent.AiExecutionId,
            integrationEvent.Confidence,
            integrationEvent.Rows.Select(ToApplicationRow).ToArray(),
            integrationEvent.Warnings
        );
        var result = await automatedExtraction.ApplyAiResultAsync(
            job.ProvisionalPricingImportId,
            integrationEvent.CorrelationId,
            job.SourceType.ToString(),
            job.EmailAttachmentId ?? job.EmailMessageId,
            job.EmailMessageId,
            job.EmailAttachmentId,
            analysis,
            new AutomatedPricingExtractionContext(
                message.Id,
                attachment?.Id,
                message.FromAddress,
                message.Subject,
                message.BodyText,
                message.BodyHtml,
                job.SourceType.ToString(),
                ForceAiAnalysis: false
            ),
            cancellationToken
        );
        var response = result.Response;
        var confidence = classifier.CalculateExtractionConfidence(
            response,
            message,
            attachment
        );
        confidence = Math.Min(
            confidence,
            Math.Clamp(integrationEvent.Confidence, 0m, 100m)
        );

        var pricingRequestId = await dbContext.ExecuteInRetryableTransactionAsync<Guid?>(
            async () =>
            {
                if (
                    !response.Success
                    || response.Rows.Count == 0
                    || response.Summary.TotalRows <= 0
                )
                {
                    var reason = result.AiErrorMessage
                        ?? response.ErrorMessage
                        ?? "DataExtraction no pudo validar las filas devueltas por AI.";
                    job.MarkNeedsReview(
                        response.ExtractionExecutionId,
                        confidence,
                        reason,
                        response.ErrorCode
                            ?? "DataExtraction.AiResultValidationFailed"
                    );
                    aiRequest.MarkCompleted();
                    await EmailJobStateCoordinator.RecalculateAsync(
                        dbContext,
                        job.EmailMessageId,
                        cancellationToken
                    );
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return null;
                }

                var hardBlockingIssues = response.Issues
                    .Where(issue => issue.IsBlocking && !IsReviewablePricingIssue(issue.Code))
                    .ToArray();
                if (hardBlockingIssues.Length > 0)
                {
                    var blockingCodes = string.Join(
                        ", ",
                        hardBlockingIssues
                            .Select(issue => issue.Code)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                    );
                    job.MarkNeedsReview(
                        response.ExtractionExecutionId,
                        confidence,
                        $"La salida de AI produjo filas, pero conserva validaciones estructurales bloqueantes: {blockingCodes}. No se envió una importación inválida a Pricing.",
                        "DataExtraction.AiResultHasBlockingIssues"
                    );
                    aiRequest.MarkCompleted();
                    await EmailJobStateCoordinator.RecalculateAsync(
                        dbContext,
                        job.EmailMessageId,
                        cancellationToken
                    );
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return null;
                }

                attachment?.MarkExtracted();
                var shouldSendToPricing =
                    account.AutoSendToPricing
                    && confidence >= account.AutoSendMinConfidence;
                if (!shouldSendToPricing)
                {
                    var reason =
                        $"Extracción validada por AI con confianza {confidence:0.##}%. "
                        + "Requiere revisión antes de crear la tarifa en Pricing.";
                    job.MarkNeedsReview(
                        response.ExtractionExecutionId,
                        confidence,
                        reason,
                        "DataExtraction.MinimumConfidenceNotMet"
                    );
                    aiRequest.MarkCompleted();
                    await EmailJobStateCoordinator.RecalculateAsync(
                        dbContext,
                        job.EmailMessageId,
                        cancellationToken
                    );
                    await dbContext.SaveChangesAsync(cancellationToken);
                    return null;
                }

                if (!response.ExtractionExecutionId.HasValue)
                {
                    throw new InvalidOperationException(
                        "La validación final no produjo ExtractionExecutionId."
                    );
                }

                var requestId = Guid.NewGuid();
                var pricingEvent =
                    new PricingImportFromExtractionRequestedIntegrationEvent(
                        Guid.NewGuid(),
                        requestId,
                        job.Id,
                        response.ExtractionExecutionId.Value,
                        job.ProvisionalPricingImportId,
                        message.Id,
                        attachment?.Id,
                        "Email",
                        message.FromAddress,
                        message.Subject,
                        payload.SourceName,
                        confidence,
                        $"{job.SourceType}:AI",
                        integrationEvent.CorrelationId,
                        response,
                        DateTime.UtcNow
                    );

                job.MarkAwaitingPricing(
                    requestId,
                    response.ExtractionExecutionId.Value,
                    confidence
                );
                aiRequest.MarkCompleted();
                await outbox.WriteAsync(
                    typeof(PricingImportFromExtractionRequestedIntegrationEvent)
                        .FullName!,
                    AsyncEmailMessageTypes.PricingRequested,
                    pricingEvent,
                    pricingEvent.CorrelationId,
                    cancellationToken
                );
                await EmailJobStateCoordinator.RecalculateAsync(
                    dbContext,
                    job.EmailMessageId,
                    cancellationToken
                );
                await dbContext.SaveChangesAsync(cancellationToken);
                return requestId;
            },
            IsolationLevel.ReadCommitted,
            cancellationToken
        );

        if (!pricingRequestId.HasValue)
        {
            return;
        }

        logger.LogInformation(
            "Resultado AI validado y enviado de forma durable a Pricing. "
                + "Trabajo {EmailExtractionJobId}; AI request {AiRequestId}; "
                + "AI execution {AiExecutionId}; Pricing request {PricingRequestId}; "
                + "CorrelationId {CorrelationId}; confianza {Confidence}.",
            job.Id,
            integrationEvent.RequestId,
            integrationEvent.AiExecutionId,
            pricingRequestId.Value,
            integrationEvent.CorrelationId,
            confidence
        );
    }

    private static bool IsActiveRequest(
        Guid? activeRequestId,
        string? activeRequestHash,
        AiPricingEmailAnalysisCompletedIntegrationEvent integrationEvent
    )
    {
        return activeRequestId == integrationEvent.RequestId
            && !string.IsNullOrWhiteSpace(activeRequestHash)
            && string.Equals(
                activeRequestHash,
                integrationEvent.RequestHash,
                StringComparison.Ordinal
            );
    }

    private static bool IsReviewablePricingIssue(string code)
    {
        return ReviewablePricingIssueCodes.Contains(code)
            || code.StartsWith("unknown_", StringComparison.OrdinalIgnoreCase);
    }

    private static AiPricingEmailRow ToApplicationRow(
        AiPricingEmailResultRow row
    )
    {
        return new AiPricingEmailRow(
            row.Pol,
            row.Poe,
            row.Pod,
            row.ContainerType,
            row.Carrier,
            row.Agent,
            row.Commodity,
            row.Currency,
            row.FreeDays,
            row.TransitDays,
            row.ValidFrom,
            row.ValidTo,
            row.OceanFreight,
            row.OriginCharges,
            row.DestinationCharges,
            row.Surcharges,
            row.TotalCost,
            row.TotalSale,
            row.Profit,
            row.Margin,
            row.SpaceComment,
            row.Remarks
        );
    }
}
