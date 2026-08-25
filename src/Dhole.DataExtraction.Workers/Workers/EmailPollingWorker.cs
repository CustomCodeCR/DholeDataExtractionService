using System.Collections.Concurrent;
using System.Net.Sockets;
using CustomCodeFramework.Workers.Abstractions;
using Dhole.DataExtraction.Application.Abstractions.Emails;
using Dhole.DataExtraction.Domain.Emails.Entities;
using Dhole.DataExtraction.Domain.Extraction.Enums;
using Dhole.DataExtraction.Infrastructure.Files;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Workers.Workers;

internal sealed class EmailPollingWorker(
    ServiceDbContext dbContext,
    IEmailReader emailReader,
    IEmailSecretResolver secretResolver,
    IEmailFileStorage fileStorage,
    IEmailRateClassifier classifier,
    IConfiguration configuration,
    ILogger<EmailPollingWorker> logger
) : IBackgroundWorker
{
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> AccountGates = new();

    public string Name => "data-extraction.email-polling";

    public async Task ExecuteAsync(IWorkerExecutionContext context, CancellationToken cancellationToken)
    {
        if (!bool.TryParse(configuration["EmailIngestion:Enabled"], out var enabled) || !enabled)
        {
            logger.LogDebug(
                "{WorkerName} está desactivado por EmailIngestion:Enabled=false.",
                Name
            );
            return;
        }

        try
        {
            var maxMessages = ReadPositiveInt(
                configuration["EmailIngestion:MaxMessagesPerSync"],
                25
            );
            var now = DateTime.UtcNow;

            // PollingIntervalMinutes existía en la entidad, pero el worker no lo respetaba.
            // El framework periódico podía abrir conexiones IMAP varias veces por minuto,
            // agotando sockets/recursos del sistema y provocando EAGAIN:
            // "Resource temporarily unavailable".
            var accountSchedules = await dbContext.EmailIngestionAccounts
                .AsNoTracking()
                .Where(x => x.IsActive && !x.IsDeleted)
                .OrderBy(x => x.EmailAddress)
                .Select(x => new
                {
                    x.Id,
                    x.EmailAddress,
                    x.PollingIntervalMinutes,
                    x.LastSyncAt,
                    x.LastSyncError,
                })
                .ToListAsync(cancellationToken);

            var failureRetryIntervalMinutes = ReadPositiveInt(
                configuration["EmailIngestion:Imap:FailureRetryIntervalMinutes"],
                2
            );

            var dueAccounts = accountSchedules
                .Where(item =>
                {
                    var intervalMinutes = string.IsNullOrWhiteSpace(item.LastSyncError)
                        ? Math.Max(1, item.PollingIntervalMinutes)
                        : failureRetryIntervalMinutes;

                    return !item.LastSyncAt.HasValue
                        || item.LastSyncAt.Value <= now.AddMinutes(-intervalMinutes);
                })
                .ToArray();

            foreach (var schedule in dueAccounts)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    logger.LogInformation(
                        "Se canceló la sincronización de correos porque el worker está terminando."
                    );
                    break;
                }

                var accountGate = AccountGates.GetOrAdd(
                    schedule.Id,
                    static _ => new SemaphoreSlim(1, 1)
                );

                // Evita solapamiento si el scheduler vuelve a ejecutar el worker antes de
                // terminar una lectura anterior del mismo buzón.
                if (!await accountGate.WaitAsync(0, cancellationToken))
                {
                    logger.LogDebug(
                        "Se omitió el buzón {EmailAddress} porque ya tiene una sincronización activa.",
                        schedule.EmailAddress
                    );
                    continue;
                }

                try
                {
                    await PollAccountAsync(
                        schedule.Id,
                        schedule.EmailAddress,
                        maxMessages,
                        cancellationToken
                    );
                }
                finally
                {
                    dbContext.ChangeTracker.Clear();
                    accountGate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation(
                "La ejecución de polling de correos fue cancelada de forma controlada."
            );
        }
    }

    private async Task PollAccountAsync(
        Guid accountId,
        string emailAddress,
        int maxMessages,
        CancellationToken stoppingToken
    )
    {
        var mailboxTimeoutSeconds = ReadPositiveInt(
            configuration["EmailIngestion:Imap:MailboxTimeoutSeconds"],
            300
        );
        var maxAttempts = ReadPositiveInt(
            configuration["EmailIngestion:Imap:TransientRetryCount"],
            3
        );
        var baseDelaySeconds = ReadPositiveInt(
            configuration["EmailIngestion:Imap:TransientRetryBaseDelaySeconds"],
            5
        );
        var maxDelaySeconds = ReadPositiveInt(
            configuration["EmailIngestion:Imap:TransientRetryMaxDelaySeconds"],
            30
        );

        using var mailboxTimeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        mailboxTimeout.CancelAfter(TimeSpan.FromSeconds(mailboxTimeoutSeconds));
        var operationToken = mailboxTimeout.Token;

        Exception? lastException = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                dbContext.ChangeTracker.Clear();
                var account = await dbContext.EmailIngestionAccounts.FirstOrDefaultAsync(
                    item => item.Id == accountId && item.IsActive && !item.IsDeleted,
                    operationToken
                );

                if (account is null)
                {
                    return;
                }

                await PollAccountOnceAsync(account, maxMessages, operationToken);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                logger.LogInformation(
                    "Se canceló la lectura del buzón {EmailAddress} porque el worker está terminando.",
                    emailAddress
                );
                return;
            }
            catch (OperationCanceledException) when (mailboxTimeout.IsCancellationRequested)
            {
                var message = $"La lectura IMAP excedió el límite total de {mailboxTimeoutSeconds} segundos.";
                logger.LogWarning("{Message} Buzón: {EmailAddress}.", message, emailAddress);
                await TryMarkSyncFailedAsync(accountId, emailAddress, message);
                return;
            }
            catch (Exception exception) when (
                IsTransientImapException(exception) && attempt < maxAttempts
            )
            {
                lastException = exception;
                dbContext.ChangeTracker.Clear();

                var delaySeconds = Math.Min(
                    maxDelaySeconds,
                    baseDelaySeconds * (int)Math.Pow(2, attempt - 1)
                );
                var jitterMilliseconds = Random.Shared.Next(100, 750);

                logger.LogWarning(
                    exception,
                    "Fallo temporal IMAP en {EmailAddress}. Reintento {NextAttempt}/{MaxAttempts} en {DelaySeconds}s.",
                    emailAddress,
                    attempt + 1,
                    maxAttempts,
                    delaySeconds
                );

                await Task.Delay(
                    TimeSpan.FromSeconds(delaySeconds)
                        + TimeSpan.FromMilliseconds(jitterMilliseconds),
                    operationToken
                );
            }
            catch (Exception exception)
            {
                lastException = exception;
                break;
            }
        }

        var finalMessage = BuildSyncErrorMessage(lastException, maxAttempts);
        logger.LogError(
            lastException,
            "Falló la lectura del buzón {EmailAddress} después de {AttemptCount} intento(s). {ErrorMessage}",
            emailAddress,
            maxAttempts,
            finalMessage
        );
        await TryMarkSyncFailedAsync(accountId, emailAddress, finalMessage);
    }

    private async Task PollAccountOnceAsync(
        EmailIngestionAccount account,
        int maxMessages,
        CancellationToken cancellationToken
    )
    {
        var password = secretResolver.ResolvePassword(account);
        var messages = await emailReader.ReadNewMessagesAsync(
            account,
            password,
            maxMessages,
            cancellationToken
        );
        long? maxUid = null;

        foreach (var incoming in messages.OrderBy(x => x.Uid ?? 0))
        {
            cancellationToken.ThrowIfCancellationRequested();

            maxUid = incoming.Uid.HasValue
                && (!maxUid.HasValue || incoming.Uid.Value > maxUid.Value)
                    ? incoming.Uid.Value
                    : maxUid;

            var existing = await dbContext.EmailMessages.AnyAsync(
                x => x.EmailIngestionAccountId == account.Id
                    && x.ExternalMessageId == incoming.ExternalMessageId
                    && !x.IsDeleted,
                cancellationToken
            );

            if (existing)
            {
                continue;
            }

            await StoreEmailAsync(account, incoming, cancellationToken);
        }

        account.MarkSyncSucceeded(maxUid);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task TryMarkSyncFailedAsync(
        Guid accountId,
        string emailAddress,
        string errorMessage
    )
    {
        try
        {
            dbContext.ChangeTracker.Clear();
            using var saveTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var account = await dbContext.EmailIngestionAccounts.FirstOrDefaultAsync(
                item => item.Id == accountId && !item.IsDeleted,
                saveTimeout.Token
            );

            if (account is not null)
            {
                account.MarkSyncFailed(errorMessage);
                await dbContext.SaveChangesAsync(saveTimeout.Token);
            }
        }
        catch (Exception saveException)
        {
            dbContext.ChangeTracker.Clear();
            logger.LogError(
                saveException,
                "No fue posible guardar el fallo de sincronización del buzón {EmailAddress}.",
                emailAddress
            );
        }
    }

    private static bool IsTransientImapException(Exception exception)
    {
        foreach (var current in EnumerateExceptionChain(exception))
        {
            if (current is TimeoutException)
            {
                return true;
            }

            if (current is SocketException socketException)
            {
                return socketException.SocketErrorCode is
                    SocketError.TryAgain or
                    SocketError.WouldBlock or
                    SocketError.TimedOut or
                    SocketError.ConnectionAborted or
                    SocketError.ConnectionReset or
                    SocketError.NetworkDown or
                    SocketError.NetworkReset or
                    SocketError.NetworkUnreachable or
                    SocketError.HostDown or
                    SocketError.HostUnreachable or
                    SocketError.NoBufferSpaceAvailable or
                    SocketError.TooManyOpenSockets;
            }

            if (current is IOException ioException)
            {
                var message = ioException.Message;
                if (
                    message.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("temporalmente no disponible", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("connection reset", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("connection aborted", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("closed the connection", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("cerró la conexión", StringComparison.OrdinalIgnoreCase)
                )
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string BuildSyncErrorMessage(Exception? exception, int attemptCount)
    {
        if (exception is null)
        {
            return "No fue posible sincronizar el buzón por un error desconocido.";
        }

        if (IsTransientImapException(exception))
        {
            return $"El servidor de correo o la red no estaban disponibles temporalmente. "
                + $"Se agotaron {attemptCount} intentos y la cuenta se volverá a revisar en su próximo intervalo.";
        }

        var rootMessage = EnumerateExceptionChain(exception)
            .Select(item => item.Message?.Trim())
            .LastOrDefault(message => !string.IsNullOrWhiteSpace(message));

        return string.IsNullOrWhiteSpace(rootMessage)
            ? "No fue posible sincronizar el buzón."
            : rootMessage;
    }

    private static IEnumerable<Exception> EnumerateExceptionChain(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    private async Task StoreEmailAsync(
        EmailIngestionAccount account,
        EmailMessageReadModel incoming,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var emailMessageId = Guid.NewGuid();
        var rawPath = await fileStorage.SaveRawEmailAsync(
            emailMessageId,
            incoming.RawContent,
            cancellationToken
        );

        var message = EmailMessage.Create(
            emailMessageId,
            account.Id,
            incoming.ExternalMessageId,
            incoming.Uid,
            incoming.MessageIdHeader,
            incoming.FromName,
            incoming.FromAddress,
            incoming.ToAddresses,
            incoming.CcAddresses,
            incoming.Subject,
            incoming.BodyText,
            incoming.BodyHtml,
            incoming.ReceivedAt,
            incoming.Attachments.Count > 0,
            rawPath,
            null
        );

        // Las FK existen en PostgreSQL, pero el modelo histórico de EF no declara
        // estas relaciones. Persistimos explícitamente cada nivel para garantizar
        // EmailMessage -> EmailAttachment -> EmailExtractionJob.
        dbContext.EmailMessages.Add(message);
        await dbContext.SaveChangesAsync(cancellationToken);

        var attachments = new List<EmailAttachment>();
        var seenAttachmentHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var incomingAttachment in incoming.Attachments)
        {
            var fileHash = FileHashCalculator.ComputeSha256(incomingAttachment.Content);
            if (!seenAttachmentHashes.Add(fileHash))
            {
                logger.LogDebug(
                    "Se omitió adjunto duplicado por hash en correo {ExternalMessageId}: {FileName}.",
                    incoming.ExternalMessageId,
                    incomingAttachment.FileName
                );
                continue;
            }

            var sourceFileType = FileTypeDetector.Detect(
                incomingAttachment.FileName,
                incomingAttachment.ContentType,
                incomingAttachment.Content
            );

            var attachmentId = Guid.NewGuid();
            var storagePath = await fileStorage.SaveAttachmentAsync(
                message.Id,
                attachmentId,
                incomingAttachment.FileName,
                incomingAttachment.Content,
                cancellationToken
            );

            var attachment = EmailAttachment.Create(
                attachmentId,
                message.Id,
                incomingAttachment.FileName,
                incomingAttachment.ContentType,
                Path.GetExtension(incomingAttachment.FileName),
                incomingAttachment.Content.LongLength,
                fileHash,
                storagePath,
                sourceFileType
            );

            attachments.Add(attachment);
            dbContext.EmailAttachments.Add(attachment);
        }

        if (attachments.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        if (!IsAllowedSender(account, message.FromAddress))
        {
            message.MarkNeedsReview("El remitente no está en la lista blanca de esta cuenta de correo.");
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        var classification = classifier.Classify(message, attachments, account);
        if (!classification.ContainsRates)
        {
            message.MarkIgnored(classification.Reason);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        if (!account.AutoProcess)
        {
            message.MarkNeedsReview("La cuenta está configurada para revisión manual antes de extraer.");
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        message.MarkQueued(classification.ConfidenceScore, classification.Reason);

        foreach (var attachmentId in classification.AttachmentIdsToProcess)
        {
            dbContext.EmailExtractionJobs.Add(EmailExtractionJob.CreateAttachmentJob(message.Id, attachmentId));
        }

        if (classification.ProcessBody)
        {
            dbContext.EmailExtractionJobs.Add(EmailExtractionJob.CreateBodyJob(message.Id));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static bool IsAllowedSender(EmailIngestionAccount account, string fromAddress)
    {
        if (string.IsNullOrWhiteSpace(account.AllowedSenders))
        {
            return true;
        }

        var from = fromAddress.Trim().ToLowerInvariant();
        var tokens = account.AllowedSenders
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.ToLowerInvariant())
            .ToArray();

        return tokens.Any(token =>
            token == "*"
            || token == from
            || (token.StartsWith('@') && from.EndsWith(token, StringComparison.OrdinalIgnoreCase))
            || (token.StartsWith("*@") && from.EndsWith(token[1..], StringComparison.OrdinalIgnoreCase))
        );
    }

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }
}
