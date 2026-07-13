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
    public string Name => "data-extraction.email-polling";

    public async Task ExecuteAsync(IWorkerExecutionContext context, CancellationToken cancellationToken)
    {
        var maxMessages = ReadPositiveInt(configuration["EmailIngestion:MaxMessagesPerSync"], 25);

        var accounts = await dbContext.EmailIngestionAccounts
            .Where(x => x.IsActive && !x.IsDeleted)
            .OrderBy(x => x.EmailAddress)
            .ToListAsync(cancellationToken);

        foreach (var account in accounts)
        {
            await PollAccountAsync(account, maxMessages, cancellationToken);
        }
    }

    private async Task PollAccountAsync(
        EmailIngestionAccount account,
        int maxMessages,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var password = secretResolver.ResolvePassword(account);
            var messages = await emailReader.ReadNewMessagesAsync(account, password, maxMessages, cancellationToken);
            long? maxUid = null;

            foreach (var incoming in messages.OrderBy(x => x.Uid ?? 0))
            {
                maxUid = incoming.Uid.HasValue && (!maxUid.HasValue || incoming.Uid.Value > maxUid.Value)
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
        catch (Exception exception)
        {
            logger.LogError(exception, "Falló la lectura del buzón {EmailAddress}.", account.EmailAddress);
            account.MarkSyncFailed(exception.Message);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task StoreEmailAsync(
        EmailIngestionAccount account,
        EmailMessageReadModel incoming,
        CancellationToken cancellationToken
    )
    {
        var emailMessageId = Guid.NewGuid();
        var rawPath = await fileStorage.SaveRawEmailAsync(emailMessageId, incoming.RawContent, cancellationToken);

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

        dbContext.EmailMessages.Add(message);

        var attachments = new List<EmailAttachment>();
        var seenAttachmentHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var incomingAttachment in incoming.Attachments)
        {
            var fileHash = FileHashCalculator.ComputeSha256(incomingAttachment.Content);
            if (!seenAttachmentHashes.Add(fileHash))
            {
                logger.LogDebug("Se omitió adjunto duplicado por hash en correo {ExternalMessageId}: {FileName}.", incoming.ExternalMessageId, incomingAttachment.FileName);
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

        if (!IsAllowedSender(account, message.FromAddress))
        {
            message.MarkNeedsReview("El remitente no está en la lista blanca de esta cuenta de correo.");
            return;
        }

        var classification = classifier.Classify(message, attachments, account);
        if (!classification.ContainsRates)
        {
            message.MarkIgnored(classification.Reason);
            return;
        }

        if (!account.AutoProcess)
        {
            message.MarkNeedsReview("La cuenta está configurada para revisión manual antes de extraer.");
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
