using System.Data;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CustomCodeFramework.Workers.Abstractions;
using Dhole.DataExtraction.Application.Abstractions.Emails;
using Dhole.DataExtraction.Application.Abstractions.Extraction;
using Dhole.DataExtraction.Application.Abstractions.Messaging;
using Dhole.DataExtraction.Contracts.AsyncEmail;
using Dhole.DataExtraction.Contracts.Extraction;
using Dhole.DataExtraction.Domain.Emails;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using Dhole.DataExtraction.Infrastructure.Email;
using Dhole.DataExtraction.Infrastructure.Files;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Workers.Workers;

internal sealed class EmailExtractionWorker(
    ServiceDbContext dbContext,
    IEmailFileStorage fileStorage,
    IAutomatedPricingExtractionService automatedExtraction,
    IEmailRateClassifier classifier,
    IIntegrationEventOutboxWriter outbox,
    IConfiguration configuration,
    ILogger<EmailExtractionWorker> logger
) : IBackgroundWorker
{
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web
    )
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly HashSet<string> ReviewablePricingIssueCodes = new(
        StringComparer.OrdinalIgnoreCase
    )
    {
        "missing_agent",
        "unknown_agent",
        "expired_rate",
    };

    private readonly string _leaseOwner =
        $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public string Name => "data-extraction.email-extraction";

    public async Task ExecuteAsync(
        IWorkerExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        if (!ReadBoolean(configuration["EmailIngestion:Enabled"], false))
        {
            logger.LogDebug(
                "{WorkerName} está desactivado por EmailIngestion:Enabled=false.",
                Name
            );
            return;
        }

        if (!ReadBoolean(configuration["AI:AsyncEmail:Enabled"], true))
        {
            logger.LogDebug(
                "{WorkerName} está desactivado por AI:AsyncEmail:Enabled=false.",
                Name
            );
            return;
        }

        await RecoverStaleJobsAsync(cancellationToken);
        await RecoverUnsupportedAttachmentJobsAsync(cancellationToken);
        await RecoverPayloadUrlRejectedJobsAsync(cancellationToken);

        var maxJobs = ReadPositiveInt(
            configuration["EmailIngestion:MaxExtractionJobsPerRun"],
            50
        );

        for (var index = 0; index < maxJobs; index++)
        {
            dbContext.ChangeTracker.Clear();
            var job = await ClaimNextJobAsync(cancellationToken);
            if (job is null)
            {
                break;
            }

            await ProcessJobAsync(job, cancellationToken);
        }
    }

    private async Task<EmailExtractionJob?> ClaimNextJobAsync(
        CancellationToken cancellationToken
    )
    {
        var leaseMinutes = ReadPositiveInt(
            configuration["EmailIngestion:ProcessingLeaseMinutes"],
            10
        );
        var now = DateTime.UtcNow;

        return await dbContext.ExecuteInRetryableTransactionAsync<EmailExtractionJob?>(
            async () =>
            {
                var job = await dbContext.EmailExtractionJobs
                    .FromSqlInterpolated(
                        $"""
                        SELECT *
                        FROM data_extraction."EmailExtractionJobs"
                        WHERE status = 'Pending'
                          AND is_deleted = FALSE
                          AND (next_attempt_at_utc IS NULL OR next_attempt_at_utc <= {now})
                        ORDER BY created_at_utc
                        FOR UPDATE SKIP LOCKED
                        LIMIT 1
                        """
                    )
                    .FirstOrDefaultAsync(cancellationToken);

                if (job is null)
                {
                    return null;
                }

                job.MarkExtracting(_leaseOwner, now.AddMinutes(leaseMinutes));
                await dbContext.SaveChangesAsync(cancellationToken);
                return job;
            },
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
    }

    private async Task RecoverStaleJobsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var maximumAttempts = ReadPositiveInt(
            configuration["EmailIngestion:MaxExtractionAttemptCount"],
            3
        );
        var staleJobs = await dbContext.EmailExtractionJobs
            .Where(job =>
                job.Status == EmailExtractionJobStatus.Extracting
                && !job.IsDeleted
                && job.LeaseExpiresAtUtc.HasValue
                && job.LeaseExpiresAtUtc.Value < now
            )
            .OrderBy(job => job.LeaseExpiresAtUtc)
            .Take(100)
            .ToListAsync(cancellationToken);

        foreach (var job in staleJobs)
        {
            if (job.AttemptCount >= maximumAttempts)
            {
                job.MarkFailed(
                    job.ExtractionExecutionId,
                    "DataExtraction.ExtractionLeaseExpired",
                    "La extracción determinística agotó sus intentos después de perder el lease."
                );
            }
            else
            {
                job.ScheduleRetry(
                    "DataExtraction.ExtractionLeaseExpired",
                    "Se recuperó un trabajo cuyo lease de extracción venció.",
                    now
                );
            }

            await EmailJobStateCoordinator.RecalculateAsync(
                dbContext,
                job.EmailMessageId,
                cancellationToken
            );
        }

        if (staleJobs.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            logger.LogWarning(
                "Se recuperaron {JobCount} trabajos de extracción con lease vencido.",
                staleJobs.Count
            );
        }
    }


    private async Task RecoverUnsupportedAttachmentJobsAsync(
        CancellationToken cancellationToken
    )
    {
        var candidates = await (
            from job in dbContext.EmailExtractionJobs
            join attachment in dbContext.EmailAttachments
                on job.EmailAttachmentId equals (Guid?)attachment.Id
            where !job.IsDeleted
                && !attachment.IsDeleted
                && job.SourceType == EmailContentSourceType.Attachment
                && job.Status != EmailExtractionJobStatus.SentToPricing
                && job.Status != EmailExtractionJobStatus.AwaitingPricing
                && job.Status != EmailExtractionJobStatus.Ignored
                && !(
                    (attachment.SourceFileType == SourceFileType.Pdf
                        && attachment.FileExtension != null
                        && attachment.FileExtension.ToLower() == ".pdf")
                    || (attachment.SourceFileType == SourceFileType.Csv
                        && attachment.FileExtension != null
                        && attachment.FileExtension.ToLower() == ".csv")
                    || (attachment.SourceFileType == SourceFileType.Excel
                        && attachment.FileExtension != null
                        && attachment.FileExtension.ToLower() == ".xlsx")
                )
            orderby job.CreatedAtUtc
            select job
        )
            .Take(250)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return;
        }

        var messageIds = new HashSet<Guid>();
        foreach (var job in candidates)
        {
            job.MarkIgnored(
                $"El adjunto se omitió porque DataExtraction solo procesa {EmailAttachmentExtractionPolicy.SupportedTypesDescription}; las imágenes y otros formatos únicamente se almacenan."
            );
            messageIds.Add(job.EmailMessageId);
        }

        foreach (var messageId in messageIds)
        {
            await EmailJobStateCoordinator.RecalculateAsync(
                dbContext,
                messageId,
                cancellationToken
            );
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Se ignoraron {JobCount} trabajos antiguos de adjuntos no soportados.",
            candidates.Count
        );
    }

    private async Task RecoverPayloadUrlRejectedJobsAsync(
        CancellationToken cancellationToken
    )
    {
        var minimumConfidence = ReadPercentage(
            configuration["AI:AutomaticExtraction:MinimumDeterministicConfidence"],
            75m
        );
        var candidates = await (
            from job in dbContext.EmailExtractionJobs
            join message in dbContext.EmailMessages on job.EmailMessageId equals message.Id
            where !job.IsDeleted
                && !message.IsDeleted
                && (job.Status == EmailExtractionJobStatus.NeedsReview
                    || job.Status == EmailExtractionJobStatus.Failed)
                && message.ClassificationConfidence >= minimumConfidence
                && (
                    job.LastErrorCode == "AI.DataExtractionPayloadUrlRejected"
                    || (job.ErrorMessage != null
                        && job.ErrorMessage.Contains(
                            "La URL del payload no pertenece al servicio DataExtraction configurado."
                        ))
                )
            orderby job.CreatedAtUtc
            select job
        )
            .Take(250)
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return;
        }

        var messageIds = new HashSet<Guid>();
        foreach (var job in candidates)
        {
            job.Retry();
            messageIds.Add(job.EmailMessageId);
        }

        foreach (var messageId in messageIds)
        {
            await EmailJobStateCoordinator.RecalculateAsync(
                dbContext,
                messageId,
                cancellationToken
            );
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogWarning(
            "Se reencolaron {JobCount} trabajos afectados por URLs antiguas de payload.",
            candidates.Count
        );
    }

    private async Task ProcessJobAsync(
        EmailExtractionJob job,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var message = await dbContext.EmailMessages.FirstOrDefaultAsync(
                item => item.Id == job.EmailMessageId && !item.IsDeleted,
                cancellationToken
            );
            if (message is null)
            {
                await MarkTerminalFailureAsync(
                    job,
                    "DataExtraction.EmailMessageNotFound",
                    "No se encontró el correo asociado al trabajo.",
                    cancellationToken
                );
                return;
            }

            var account = await dbContext.EmailIngestionAccounts.FirstOrDefaultAsync(
                item => item.Id == message.EmailIngestionAccountId && !item.IsDeleted,
                cancellationToken
            );
            if (account is null)
            {
                await MarkTerminalFailureAsync(
                    job,
                    "DataExtraction.EmailAccountNotFound",
                    "No se encontró la cuenta de correo asociada al mensaje.",
                    cancellationToken
                );
                return;
            }

            if (
                await IgnoreUnsupportedAttachmentAsync(
                    job,
                    message,
                    cancellationToken
                )
            )
            {
                return;
            }

            if (
                job.SourceType == EmailContentSourceType.Body
                && !account.ProcessBodyEvenWithAttachments
                && await HasProcessableAttachmentAsync(message.Id, cancellationToken)
            )
            {
                job.MarkIgnored(
                    "Se omitió el cuerpo porque el correo contiene un adjunto soportado y la cuenta no permite procesar ambos formatos."
                );
                await EmailJobStateCoordinator.RecalculateAsync(
                    dbContext,
                    message.Id,
                    cancellationToken
                );
                await dbContext.SaveChangesAsync(cancellationToken);
                return;
            }

            var input = await BuildExtractionInputAsync(job, message, cancellationToken);
            var deterministicResponse = await automatedExtraction.ExtractDeterministicAsync(
                input.Request,
                cancellationToken
            );
            var deterministicConfidence = classifier.CalculateExtractionConfidence(
                deterministicResponse,
                message,
                input.Attachment
            );
            var minimumDeterministicConfidence = ReadPercentage(
                configuration[
                    "AI:AutomaticExtraction:MinimumDeterministicConfidence"
                ],
                75m
            );
            var deterministicIsUsable = IsUsable(deterministicResponse);
            var forceAiConfigured = ReadBoolean(
                configuration["AI:AutomaticExtraction:ForceAiForEmail"],
                false
            );
            var requiresAiForComplexStructure = RequiresAiForComplexPricingEmail(
                message,
                job.SourceType,
                deterministicResponse
            );
            var forceAi = forceAiConfigured || requiresAiForComplexStructure;
            var classificationConfidence = message.ClassificationConfidence ?? 0m;
            var useClassificationConfidenceForBypass = ReadBoolean(
                configuration[
                    "AI:AutomaticExtraction:UseClassificationConfidenceForBypass"
                ],
                false
            );
            var hasHardBlockingIssues = HasHardBlockingIssues(
                deterministicResponse
            );
            var classificationAllowsBypass =
                useClassificationConfidenceForBypass
                && classificationConfidence >= minimumDeterministicConfidence;
            var deterministicAllowsBypass =
                deterministicConfidence >= minimumDeterministicConfidence;
            var bypassAiWhenDeterministicRowsExist = ReadBoolean(
                configuration[
                    "AI:AutomaticExtraction:BypassAiWhenDeterministicRowsExist"
                ],
                true
            );
            var deterministicRowsAllowBypass =
                bypassAiWhenDeterministicRowsExist
                && job.SourceType == EmailContentSourceType.Body
                && deterministicIsUsable
                && !hasHardBlockingIssues;

            if (
                !forceAi
                && classificationAllowsBypass
                && deterministicIsUsable
                && !hasHardBlockingIssues
            )
            {
                logger.LogInformation(
                    "Trabajo {EmailExtractionJobId} se resolverá sin AI por confianza de clasificación y extracción determinística válida. "
                        + "Clasificación {ClassificationConfidence:0.##}%; "
                        + "determinística {DeterministicConfidence:0.##}%; umbral {Threshold:0.##}%.",
                    job.Id,
                    classificationConfidence,
                    deterministicConfidence,
                    minimumDeterministicConfidence
                );

                await CompleteWithDeterministicResultAsync(
                    job,
                    message,
                    account,
                    input,
                    deterministicResponse,
                    deterministicConfidence,
                    cancellationToken
                );
                return;
            }

            if (
                !forceAi
                && deterministicIsUsable
                && (deterministicAllowsBypass || deterministicRowsAllowBypass)
                && !hasHardBlockingIssues
            )
            {
                var bypassReason = deterministicRowsAllowBypass
                    && !deterministicAllowsBypass
                        ? "filas determinísticas revisables"
                        : "confianza determinística";

                logger.LogInformation(
                    "Trabajo {EmailExtractionJobId} se resolverá sin AI por {BypassReason}. "
                        + "Clasificación {ClassificationConfidence:0.##}%; "
                        + "determinística {DeterministicConfidence:0.##}%; umbral {Threshold:0.##}%.",
                    job.Id,
                    bypassReason,
                    classificationConfidence,
                    deterministicConfidence,
                    minimumDeterministicConfidence
                );

                await CompleteWithDeterministicResultAsync(
                    job,
                    message,
                    account,
                    input,
                    deterministicResponse,
                    deterministicConfidence,
                    cancellationToken
                );
                return;
            }

            logger.LogInformation(
                "Trabajo {EmailExtractionJobId} requiere AI. "
                    + "Confianza de clasificación {ClassificationConfidence:0.##}%; "
                    + "confianza determinística {DeterministicConfidence:0.##}%; "
                    + "umbral {Threshold:0.##}%; filas {RowCount}; usable {IsUsable}; "
                    + "bloqueos duros {HasHardBlockingIssues}; estructura compleja {RequiresAiForComplexStructure}.",
                job.Id,
                classificationConfidence,
                deterministicConfidence,
                minimumDeterministicConfidence,
                deterministicResponse.Rows.Count,
                deterministicIsUsable,
                hasHardBlockingIssues,
                requiresAiForComplexStructure
            );

            var prepared = await automatedExtraction.PrepareAiRequestAsync(
                input.Request,
                deterministicResponse,
                new AutomatedPricingExtractionContext(
                    message.Id,
                    input.Attachment?.Id,
                    message.FromAddress,
                    message.Subject,
                    message.BodyText,
                    message.BodyHtml,
                    job.SourceType.ToString(),
                    ForceAiAnalysis: false
                ),
                imageStoragePath: null,
                cancellationToken
            );
            var payloadJson = JsonSerializer.Serialize(prepared.Payload, JsonOptions);
            var aiRequest = EmailAiAnalysisRequest.Create(
                job.Id,
                message.Id,
                input.Attachment?.Id,
                deterministicResponse.ExtractionExecutionId,
                job.ProvisionalPricingImportId,
                input.Request.CorrelationId,
                prepared.RequestHash,
                payloadJson,
                imageStoragePath: null,
                imageContentType: null
            );
            var integrationEvent =
                new AiPricingEmailAnalysisRequestedIntegrationEvent(
                    Guid.NewGuid(),
                    aiRequest.Id,
                    job.Id,
                    message.Id,
                    input.Attachment?.Id,
                    job.ProvisionalPricingImportId,
                    deterministicResponse.ExtractionExecutionId,
                    input.Request.CorrelationId,
                    prepared.RequestHash,
                    BuildPayloadUrl(aiRequest.Id),
                    DateTime.UtcNow
                );

            await dbContext.ExecuteInRetryableTransactionAsync(
                async () =>
                {
                    message.MarkProcessing();
                    await dbContext.EmailAiAnalysisRequests.AddAsync(
                        aiRequest,
                        cancellationToken
                    );
                    job.MarkAwaitingAi(
                        aiRequest.Id,
                        deterministicResponse.ExtractionExecutionId,
                        prepared.RequestHash
                    );
                    await outbox.WriteAsync(
                        typeof(AiPricingEmailAnalysisRequestedIntegrationEvent).FullName!,
                        AsyncEmailMessageTypes.AiRequested,
                        integrationEvent,
                        integrationEvent.CorrelationId,
                        cancellationToken
                    );
                    await dbContext.SaveChangesAsync(cancellationToken);
                },
                IsolationLevel.ReadCommitted,
                cancellationToken
            );

            logger.LogInformation(
                "Trabajo {EmailExtractionJobId} preparado para AI sin espera bloqueante. "
                    + "Solicitud {AiRequestId}; correo {EmailMessageId}; adjunto {EmailAttachmentId}; "
                    + "CorrelationId {CorrelationId}; RequestHash {RequestHash}.",
                job.Id,
                aiRequest.Id,
                message.Id,
                input.Attachment?.Id,
                input.Request.CorrelationId,
                prepared.RequestHash
            );
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Falló la preparación asíncrona del trabajo {EmailExtractionJobId}.",
                job.Id
            );
            await PersistRetryOrFailureAsync(job.Id, exception);
        }
    }

    private async Task<bool> IgnoreUnsupportedAttachmentAsync(
        EmailExtractionJob job,
        EmailMessage message,
        CancellationToken cancellationToken
    )
    {
        if (
            job.SourceType != EmailContentSourceType.Attachment
            || !job.EmailAttachmentId.HasValue
        )
        {
            return false;
        }

        var attachment = await dbContext.EmailAttachments.FirstOrDefaultAsync(
            item => item.Id == job.EmailAttachmentId.Value && !item.IsDeleted,
            cancellationToken
        );
        if (attachment is null || EmailAttachmentExtractionPolicy.IsSupported(attachment))
        {
            return false;
        }

        job.MarkIgnored(
            $"El adjunto '{attachment.FileName}' no se extrajo. Solo se permiten {EmailAttachmentExtractionPolicy.SupportedTypesDescription}; las imágenes y demás archivos quedan almacenados sin extracción."
        );
        await EmailJobStateCoordinator.RecalculateAsync(
            dbContext,
            message.Id,
            cancellationToken
        );
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task CompleteHighConfidenceWithoutAiAsync(
        EmailExtractionJob job,
        EmailMessage message,
        ExtractPricingDataResponse deterministicResponse,
        decimal classificationConfidence,
        decimal deterministicConfidence,
        bool hasHardBlockingIssues,
        CancellationToken cancellationToken
    )
    {
        var reason = deterministicResponse.Rows.Count == 0
            ? $"El correo fue clasificado con {classificationConfidence:0.##}% de confianza, pero la extracción determinística no produjo filas. Se envía a revisión sin llamar a AI."
            : $"El correo fue clasificado con {classificationConfidence:0.##}% de confianza, pero presenta validaciones bloqueantes. Se envía a revisión sin llamar a AI.";
        var errorCode = deterministicResponse.ErrorCode
            ?? (hasHardBlockingIssues
                ? "DataExtraction.HighConfidenceBlockingIssues"
                : "DataExtraction.HighConfidenceNoRows");

        job.MarkNeedsReview(
            deterministicResponse.ExtractionExecutionId,
            Math.Max(classificationConfidence, deterministicConfidence),
            reason,
            errorCode
        );
        await EmailJobStateCoordinator.RecalculateAsync(
            dbContext,
            message.Id,
            cancellationToken
        );
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogWarning(
            "Trabajo {EmailExtractionJobId} no usará AI pese a no ser utilizable. "
                + "Clasificación {ClassificationConfidence:0.##}%; determinística {DeterministicConfidence:0.##}%; "
                + "filas {RowCount}; bloqueos {HasHardBlockingIssues}.",
            job.Id,
            classificationConfidence,
            deterministicConfidence,
            deterministicResponse.Rows.Count,
            hasHardBlockingIssues
        );
    }

    private async Task CompleteWithDeterministicResultAsync(
        EmailExtractionJob job,
        EmailMessage message,
        EmailIngestionAccount account,
        EmailExtractionInput input,
        ExtractPricingDataResponse response,
        decimal confidence,
        CancellationToken cancellationToken
    )
    {
        if (!response.ExtractionExecutionId.HasValue)
        {
            job.MarkNeedsReview(
                response.ExtractionExecutionId,
                confidence,
                "La normalización determinística superó el umbral, pero no produjo ExtractionExecutionId.",
                "DataExtraction.MissingExtractionExecutionId"
            );
            await EmailJobStateCoordinator.RecalculateAsync(
                dbContext,
                job.EmailMessageId,
                cancellationToken
            );
            await dbContext.SaveChangesAsync(cancellationToken);
            return;
        }

        input.Attachment?.MarkExtracted();
        var shouldSendToPricing =
            account.AutoSendToPricing
            && confidence >= account.AutoSendMinConfidence;

        if (!shouldSendToPricing)
        {
            job.MarkNeedsReview(
                response.ExtractionExecutionId,
                confidence,
                $"Extracción determinística completada con confianza {confidence:0.##}%. "
                    + "No se invocó AI porque el resultado fue utilizable y no presentó bloqueos duros, "
                    + "pero requiere revisión antes de crear la tarifa en Pricing.",
                "DataExtraction.DeterministicReviewRequired"
            );
            await EmailJobStateCoordinator.RecalculateAsync(
                dbContext,
                job.EmailMessageId,
                cancellationToken
            );
            await dbContext.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Trabajo {EmailExtractionJobId} resuelto sin AI con {Confidence:0.##}% y enviado a revisión.",
                job.Id,
                confidence
            );
            return;
        }

        var requestId = Guid.NewGuid();
        var pricingEvent = new PricingImportFromExtractionRequestedIntegrationEvent(
            Guid.NewGuid(),
            requestId,
            job.Id,
            response.ExtractionExecutionId.Value,
            job.ProvisionalPricingImportId,
            message.Id,
            input.Attachment?.Id,
            "Email",
            message.FromAddress,
            message.Subject,
            input.Request.OriginalFileName,
            confidence,
            $"{job.SourceType}:Deterministic",
            input.Request.CorrelationId,
            response,
            DateTime.UtcNow
        );

        await dbContext.ExecuteInRetryableTransactionAsync(
            async () =>
            {
                job.MarkAwaitingPricingFromDeterministic(
                    requestId,
                    response.ExtractionExecutionId.Value,
                    confidence
                );
                await outbox.WriteAsync(
                    typeof(PricingImportFromExtractionRequestedIntegrationEvent).FullName!,
                    AsyncEmailMessageTypes.PricingRequested,
                    pricingEvent,
                    pricingEvent.CorrelationId,
                    cancellationToken
                );
                message.MarkProcessing();
                await dbContext.SaveChangesAsync(cancellationToken);
            },
            IsolationLevel.ReadCommitted,
            cancellationToken
        );

        logger.LogInformation(
            "Trabajo {EmailExtractionJobId} resuelto sin AI y enviado a Pricing. "
                + "Confianza {Confidence:0.##}%; ExtractionExecutionId {ExtractionExecutionId}; "
                + "PricingRequestId {PricingRequestId}.",
            job.Id,
            confidence,
            response.ExtractionExecutionId,
            requestId
        );
    }

    private static bool HasHardBlockingIssues(
        ExtractPricingDataResponse response
    )
    {
        return response.Issues.Any(issue =>
            issue.IsBlocking && !IsReviewablePricingIssue(issue.Code)
        );
    }

    private static bool IsReviewablePricingIssue(string code)
    {
        return ReviewablePricingIssueCodes.Contains(code)
            || code.StartsWith("unknown_", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUsable(ExtractPricingDataResponse response)
    {
        return response.Success
            && response.Rows.Count > 0
            && response.Summary.TotalRows > 0;
    }

    private async Task PersistRetryOrFailureAsync(Guid jobId, Exception exception)
    {
        try
        {
            dbContext.ChangeTracker.Clear();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var job = await dbContext.EmailExtractionJobs.FirstOrDefaultAsync(
                item => item.Id == jobId && !item.IsDeleted,
                timeout.Token
            );
            if (job is null)
            {
                return;
            }
            if (job.Status != EmailExtractionJobStatus.Extracting)
            {
                logger.LogInformation(
                    "El trabajo {EmailExtractionJobId} ya quedó en estado {Status}; "
                        + "no se sobrescribe después de un resultado de commit ambiguo.",
                    job.Id,
                    job.Status
                );
                return;
            }

            var maximumAttempts = ReadPositiveInt(
                configuration["EmailIngestion:MaxExtractionAttemptCount"],
                3
            );
            var errorMessage = Limit(exception.GetBaseException().Message, 4000);
            if (job.AttemptCount >= maximumAttempts)
            {
                job.MarkFailed(
                    job.ExtractionExecutionId,
                    "DataExtraction.AsyncEmailPreparationFailed",
                    errorMessage
                );
            }
            else
            {
                var delaySeconds = ReadPositiveInt(
                    configuration["EmailIngestion:ExtractionRetryDelaySeconds"],
                    30
                );
                job.ScheduleRetry(
                    "DataExtraction.AsyncEmailPreparationFailed",
                    errorMessage,
                    DateTime.UtcNow.AddSeconds(delaySeconds)
                );
            }

            await EmailJobStateCoordinator.RecalculateAsync(
                dbContext,
                job.EmailMessageId,
                timeout.Token
            );
            await dbContext.SaveChangesAsync(timeout.Token);
        }
        catch (Exception persistenceException)
        {
            dbContext.ChangeTracker.Clear();
            logger.LogCritical(
                persistenceException,
                "No se pudo persistir el retry/fallo del trabajo {EmailExtractionJobId}.",
                jobId
            );
        }
    }

    private async Task MarkTerminalFailureAsync(
        EmailExtractionJob job,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken
    )
    {
        job.MarkFailed(job.ExtractionExecutionId, errorCode, errorMessage);
        await EmailJobStateCoordinator.RecalculateAsync(
            dbContext,
            job.EmailMessageId,
            cancellationToken
        );
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<EmailExtractionInput> BuildExtractionInputAsync(
        EmailExtractionJob job,
        EmailMessage message,
        CancellationToken cancellationToken
    )
    {
        if (job.SourceType == EmailContentSourceType.Attachment)
        {
            if (!job.EmailAttachmentId.HasValue)
            {
                throw new InvalidOperationException(
                    "El trabajo de adjunto no tiene EmailAttachmentId."
                );
            }

            var attachment = await dbContext.EmailAttachments.FirstOrDefaultAsync(
                item => item.Id == job.EmailAttachmentId.Value && !item.IsDeleted,
                cancellationToken
            ) ?? throw new InvalidOperationException(
                "No se encontró el adjunto asociado al trabajo."
            );
            if (!EmailAttachmentExtractionPolicy.IsSupported(attachment))
            {
                throw new InvalidOperationException(
                    $"El formato del adjunto no es compatible. Solo se permiten {EmailAttachmentExtractionPolicy.SupportedTypesDescription}."
                );
            }

            var attachmentContent = await fileStorage.ReadAsync(
                attachment.StoragePath,
                cancellationToken
            );
            var request = new ExtractionDataRequest(
                job.ProvisionalPricingImportId,
                $"email-{message.Id:N}-{attachment.Id:N}",
                attachment.FileName,
                attachment.ContentType,
                attachment.FileExtension,
                attachment.SizeBytes,
                attachment.FileHash,
                null,
                null,
                "Email Ingestion Worker",
                attachmentContent
            )
            {
                SourceOriginType = "EmailAttachment",
                SourceOriginId = attachment.Id,
                SourceEmailMessageId = message.Id,
                SourceEmailAttachmentId = attachment.Id,
                SourceEmailSubject = message.Subject,
                SourceEmailBodyText = message.BodyText,
                SourceEmailBodyHtml = message.BodyHtml,
                StoragePath = attachment.StoragePath,
            };

            return new EmailExtractionInput(request, attachment);
        }

        var body = EmailPricingContentSelector.SelectPreferredBody(
            message.BodyText,
            message.BodyHtml
        );
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new InvalidOperationException("El correo no tiene cuerpo para procesar.");
        }

        // Use a focused plain-text representation for deterministic extraction.
        // Outlook HTML frequently splits one rate sentence across several block
        // elements and includes months of quoted history in the same body.
        const string extension = ".txt";
        const string contentType = "text/plain";
        var content = Encoding.UTF8.GetBytes(body);
        var fileName = $"email-body-{message.Id:N}{extension}";
        var requestBody = new ExtractionDataRequest(
            job.ProvisionalPricingImportId,
            $"email-{message.Id:N}-body",
            fileName,
            contentType,
            extension,
            content.LongLength,
            FileHashCalculator.ComputeSha256(content),
            null,
            null,
            "Email Ingestion Worker",
            content
        )
        {
            SourceOriginType = "EmailBody",
            SourceOriginId = message.Id,
            SourceEmailMessageId = message.Id,
            SourceEmailSubject = message.Subject,
            SourceEmailBodyText = message.BodyText,
            SourceEmailBodyHtml = message.BodyHtml,
        };

        return new EmailExtractionInput(requestBody, null);
    }

    private Task<bool> HasProcessableAttachmentAsync(
        Guid emailMessageId,
        CancellationToken cancellationToken
    )
    {
        return dbContext.EmailAttachments.AnyAsync(
            attachment =>
                attachment.EmailMessageId == emailMessageId
                && !attachment.IsDeleted
                && attachment.SizeBytes > 0
                && attachment.FileExtension != null
                && (
                    (attachment.SourceFileType == SourceFileType.Pdf
                        && attachment.FileExtension.ToLower() == ".pdf")
                    || (attachment.SourceFileType == SourceFileType.Csv
                        && attachment.FileExtension.ToLower() == ".csv")
                    || (attachment.SourceFileType == SourceFileType.Excel
                        && attachment.FileExtension.ToLower() == ".xlsx")
                ),
            cancellationToken
        );
    }


    private static bool RequiresAiForComplexPricingEmail(
        EmailMessage message,
        EmailContentSourceType sourceType,
        ExtractPricingDataResponse deterministicResponse
    )
    {
        if (sourceType != EmailContentSourceType.Body)
        {
            return false;
        }

        // Known deterministic body parsers already expand grouped routes and
        // validate every row through the normal extraction pipeline. Do not send
        // a complete NAC/FAK matrix back to a local model, because the AI result
        // could truncate or replace dozens of valid route combinations.
        if (HasCompleteDeterministicEmailMatrix(deterministicResponse))
        {
            return false;
        }

        var body = string.Join(
            "\n",
            new[] { message.Subject, message.BodyText, message.BodyHtml }
                .Where(value => !string.IsNullOrWhiteSpace(value))
        );
        if (string.IsNullOrWhiteSpace(body))
        {
            return false;
        }

        var hasParallelAmountsAndCarriers =
            System.Text.RegularExpressions.Regex.IsMatch(
                body,
                @"(?:USD|EUR|CRC|\$|€|₡)\s*\d[\d,.]*\s*/\s*\d[\d,.]*",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            )
            && System.Text.RegularExpressions.Regex.IsMatch(
                body,
                @"\bCarrier\s+[A-Z0-9 .&'-]+\s*/\s*[A-Z0-9 .&'-]+",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase
            );
        var hasGroupedRoutes = System.Text.RegularExpressions.Regex.IsMatch(
            body,
            @"\bPOL\s*:\s*[^\r\n]+/[^\r\n]+",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        ) && System.Text.RegularExpressions.Regex.IsMatch(
            body,
            @"\bPOD\s*:\s*[^\r\n]+/[^\r\n]+",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        var hasScopedCommercialGroups =
            body.Contains("Below the details", StringComparison.OrdinalIgnoreCase)
            && body.Contains("COMM:", StringComparison.OrdinalIgnoreCase);
        var hasArbitraryCharges = System.Text.RegularExpressions.Regex.IsMatch(
            body,
            @"\(\s*\+?\s*arb\s+(?:USD|EUR|CRC)?\s*\d+",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );
        var deterministicContainsGroupedValues = deterministicResponse.Rows.Any(row =>
            ContainsGroupedValue(row.OriginPort)
            || ContainsGroupedValue(row.PortOfExit)
            || ContainsGroupedValue(row.DestinationPort)
            || ContainsGroupedValue(row.Carrier)
        );

        return deterministicContainsGroupedValues
            || (hasParallelAmountsAndCarriers && hasGroupedRoutes)
            || (hasGroupedRoutes && hasScopedCommercialGroups)
            || hasArbitraryCharges;
    }


    private static bool HasCompleteDeterministicEmailMatrix(
        ExtractPricingDataResponse response
    )
    {
        if (response.Rows.Count == 0)
        {
            return false;
        }

        return response.Rows.All(row =>
            row.SourceSheetName is "EMAIL NAC Narrative" or "EMAIL FCL Matrix"
            && !string.IsNullOrWhiteSpace(row.OriginPort)
            && !string.IsNullOrWhiteSpace(row.PortOfExit)
            && !string.IsNullOrWhiteSpace(row.ContainerType)
            && !string.IsNullOrWhiteSpace(row.Carrier)
            && !string.IsNullOrWhiteSpace(row.Currency)
            && row.ValidFrom.HasValue
            && row.ValidTo.HasValue
            && row.OceanFreight.HasValue
        );
    }

    private static bool ContainsGroupedValue(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && (value.Contains('/') || value.Contains(';') || value.Contains('|'));
    }

    private static string BuildPayloadUrl(Guid requestId)
    {
        return $"/api/internal/data-extraction/ai-email-requests/{requestId}";
    }

    private static bool ReadBoolean(string? value, bool fallback)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }

    private static decimal ReadPercentage(string? value, decimal fallback)
    {
        return decimal.TryParse(
            value,
            System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture,
            out var parsed
        )
            ? Math.Clamp(parsed, 0m, 100m)
            : Math.Clamp(fallback, 0m, 100m);
    }

    private static string Limit(string value, int maximumLength)
    {
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

    private sealed record EmailExtractionInput(
        ExtractionDataRequest Request,
        EmailAttachment? Attachment
    );
}
