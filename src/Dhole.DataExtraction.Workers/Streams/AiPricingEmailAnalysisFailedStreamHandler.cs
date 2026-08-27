using System.Data;
using System.Net;
using CustomCodeFramework.Redis.Streams.Abstractions;
using CustomCodeFramework.Redis.Streams.Messages;
using Dhole.DataExtraction.Application.Abstractions.Emails;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Application.Abstractions.Messaging;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Contracts.AsyncEmail;
using Dhole.DataExtraction.Domain.Emails;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Persistence.DbContexts;
using Dhole.DataExtraction.Workers.Workers;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Workers.Streams;

internal sealed class AiPricingEmailAnalysisFailedStreamHandler(
    ServiceDbContext dbContext,
    IAutomatedPricingExtractionService automatedExtraction,
    IEmailRateClassifier classifier,
    IIntegrationEventOutboxWriter outbox,
    ILogger<AiPricingEmailAnalysisFailedStreamHandler> logger
) : IRedisStreamMessageHandler
{
    private const int MaximumDeterministicRecoveryRows = 250;

    private static readonly HashSet<string> ReviewablePricingIssueCodes = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "missing_agent",
        "unknown_agent",
        "missing_destination_port",
        "same_poe_and_pod",
        "missing_currency",
        "expired_rate",
    };

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
        if (request.CompletedAtUtc.HasValue && !isRecoveringLeaseFailure)
        {
            return;
        }

        var payload =
            AsyncEmailStreamPayloadReader.ReadJson<AiPricingEmailAnalysisRequest>(
                request.PayloadJson
            );
        var message = await dbContext.EmailMessages.FirstOrDefaultAsync(
            item => item.Id == job.EmailMessageId && !item.IsDeleted,
            cancellationToken
        ) ?? throw new InvalidOperationException(
            "No se encontró el correo asociado al fallo de AI."
        );
        var attachment = job.EmailAttachmentId.HasValue
            ? await dbContext.EmailAttachments.FirstOrDefaultAsync(
                item =>
                    item.Id == job.EmailAttachmentId.Value
                    && !item.IsDeleted,
                cancellationToken
            )
            : null;

        var deterministicExecutionId =
            request.ExtractionExecutionId ?? job.ExtractionExecutionId;
        var deterministicRows = deterministicExecutionId.HasValue
            ? await dbContext.PricingExtractionRecords
                .AsNoTracking()
                .Where(item =>
                    item.ExtractionExecutionId == deterministicExecutionId.Value
                    && !item.IsDeleted
                )
                .OrderBy(item => item.SourceSheetName)
                .ThenBy(item => item.SourceRowNumber)
                .Take(MaximumDeterministicRecoveryRows)
                .ToListAsync(cancellationToken)
            : [];

        // PDF/CSV/XLSX pasan primero por DataExtraction. Si AI no encuentra filas,
        // nunca se descarta esa matriz determinística: se vuelve a normalizar y
        // validar con el mismo pipeline antes de decidir si realmente falló.
        if (deterministicRows.Count > 0)
        {
            var deterministicAnalysis = new AiPricingEmailAnalysisResult(
                true,
                integrationEvent.AiExecutionId,
                payload.PreviousConfidence,
                deterministicRows
                    .Select(item => new AiPricingEmailRow(
                        item.OriginPort,
                        item.PortOfExit,
                        item.DestinationPort,
                        item.ContainerType,
                        item.Carrier,
                        item.Agent,
                        item.Commodity,
                        item.Currency,
                        item.FreeDays,
                        item.TransitDays,
                        item.ValidFrom,
                        item.ValidTo,
                        item.OceanFreight,
                        item.OriginCharges,
                        item.DestinationCharges,
                        item.Surcharges,
                        item.TotalCost,
                        item.TotalSale,
                        item.Profit,
                        item.Margin,
                        item.SpaceComment,
                        item.Remarks
                    ))
                    .ToArray(),
                [
                    "AI no produjo filas utilizables; se conservó y revalidó la matriz extraída por DataExtraction."
                ]
            );

            var recovery = await automatedExtraction.ApplyAiResultAsync(
                job.ProvisionalPricingImportId,
                integrationEvent.CorrelationId,
                job.SourceType.ToString(),
                job.EmailAttachmentId ?? job.EmailMessageId,
                job.EmailMessageId,
                job.EmailAttachmentId,
                deterministicAnalysis,
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
            var response = recovery.Response;
            var confidence = classifier.CalculateExtractionConfidence(
                response,
                message,
                attachment
            );
            var hardBlockingIssues = response.Issues
                .Where(issue =>
                    issue.IsBlocking
                    && !IsReviewablePricingIssue(issue.Code)
                )
                .ToArray();

            if (
                response.Success
                && response.Rows.Count > 0
                && response.Summary.TotalRows > 0
                && hardBlockingIssues.Length == 0
                && response.ExtractionExecutionId.HasValue
            )
            {
                var pricingRequestId = Guid.NewGuid();
                var pricingEvent =
                    new PricingImportFromExtractionRequestedIntegrationEvent(
                        Guid.NewGuid(),
                        pricingRequestId,
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
                        $"{job.SourceType}:DeterministicFallbackAfterAi",
                        integrationEvent.CorrelationId,
                        response,
                        DateTime.UtcNow
                    );

                await dbContext.ExecuteInRetryableTransactionAsync(
                    async () =>
                    {
                        if (isRecoveringLeaseFailure)
                        {
                            job.MarkValidatingRecoveredAiResult(
                                integrationEvent.RequestId,
                                integrationEvent.AiExecutionId ?? Guid.Empty
                            );
                        }
                        else
                        {
                            job.MarkValidatingAiResult(
                                integrationEvent.RequestId,
                                integrationEvent.AiExecutionId ?? Guid.Empty
                            );
                        }

                        attachment?.MarkExtracted();
                        job.MarkAwaitingPricing(
                            pricingRequestId,
                            response.ExtractionExecutionId.Value,
                            confidence
                        );
                        request.MarkCompleted();
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
                    },
                    IsolationLevel.ReadCommitted,
                    cancellationToken
                );

                logger.LogWarning(
                    "AI no produjo filas para el trabajo {EmailExtractionJobId}, pero DataExtraction recuperó {RowCount} filas y las envió a Pricing. "
                        + "Solicitud AI {AiRequestId}; ejecución determinística {ExtractionExecutionId}; Pricing request {PricingRequestId}.",
                    job.Id,
                    response.Rows.Count,
                    integrationEvent.RequestId,
                    response.ExtractionExecutionId.Value,
                    pricingRequestId
                );
                return;
            }

            var blockingCodes = string.Join(
                ", ",
                hardBlockingIssues
                    .Select(issue => issue.Code)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
            );
            var recoveryReason =
                "AI no produjo filas utilizables, pero DataExtraction sí conservó filas del adjunto. "
                + "La revalidación determinística requiere revisión"
                + (string.IsNullOrWhiteSpace(blockingCodes)
                    ? "."
                    : $": {blockingCodes}.");

            job.MarkNeedsReview(
                response.ExtractionExecutionId ?? deterministicExecutionId,
                confidence,
                recoveryReason,
                response.ErrorCode
                    ?? "DataExtraction.DeterministicFallbackRequiresReview"
            );
            request.MarkCompleted();
            await EmailJobStateCoordinator.RecalculateAsync(
                dbContext,
                job.EmailMessageId,
                cancellationToken
            );
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogWarning(
                "AI falló para el trabajo {EmailExtractionJobId}; la matriz determinística se conservó para revisión. "
                    + "Filas originales {OriginalRowCount}; filas revalidadas {ValidatedRowCount}; código AI {ErrorCode}.",
                job.Id,
                deterministicRows.Count,
                response.Rows.Count,
                integrationEvent.ErrorCode
            );
            return;
        }

        var normalizedAiMessage = NormalizeErrorMessage(integrationEvent.ErrorMessage);
        var isUnsupportedAttachment =
            attachment is not null
            && !EmailAttachmentExtractionPolicy.IsSupported(attachment);
        var isNoPricingRows = string.Equals(
            integrationEvent.ErrorCode,
            "AI.NoPricingRows",
            StringComparison.OrdinalIgnoreCase
        );

        if (isUnsupportedAttachment)
        {
            job.MarkIgnored(
                $"El adjunto se omitió porque DataExtraction solo procesa {EmailAttachmentExtractionPolicy.SupportedTypesDescription}; las imágenes y otros formatos únicamente se almacenan."
            );
        }
        else if (isNoPricingRows)
        {
            var hasSuccessfulSibling = await dbContext.EmailExtractionJobs
                .AsNoTracking()
                .AnyAsync(item =>
                    item.EmailMessageId == job.EmailMessageId
                    && item.Id != job.Id
                    && !item.IsDeleted
                    && item.Status == EmailExtractionJobStatus.SentToPricing,
                    cancellationToken
                );

            if (hasSuccessfulSibling)
            {
                job.MarkIgnored(
                    "El contenido no produjo filas tarifarias adicionales y se omitió porque otro contenido de este correo ya fue enviado a Pricing."
                );
            }
            else
            {
                job.MarkNeedsReview(
                    job.ExtractionExecutionId,
                    payload.PreviousConfidence,
                    "AI no encontró filas tarifarias utilizables y DataExtraction tampoco produjo una matriz determinística. Revise este contenido antes de descartarlo.",
                    integrationEvent.ErrorCode
                );
            }
        }
        else
        {
            var reason =
                "La normalización asistida por AI no pudo completarse y DataExtraction tampoco produjo filas utilizables: "
                + normalizedAiMessage;
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
            "Se cerró el resultado AI del trabajo {EmailExtractionJobId} sin matriz determinística recuperable. "
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

    private static string NormalizeErrorMessage(string? value)
    {
        var decoded = WebUtility.HtmlDecode(value ?? string.Empty);
        var normalized = string.Join(
            ' ',
            decoded.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        );

        return string.IsNullOrWhiteSpace(normalized)
            ? "AI no devolvió un detalle adicional."
            : normalized;
    }

    private static bool IsReviewablePricingIssue(string code)
    {
        return ReviewablePricingIssueCodes.Contains(code)
            || code.StartsWith("unknown_", StringComparison.OrdinalIgnoreCase);
    }
}
