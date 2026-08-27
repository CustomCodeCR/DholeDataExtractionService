using CustomCodeFramework.Redis.Streams.Abstractions;
using CustomCodeFramework.Redis.Streams.Messages;
using Dhole.DataExtraction.Contracts.AsyncEmail;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Persistence.DbContexts;
using Dhole.DataExtraction.Workers.Workers;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Workers.Streams;

internal sealed class PricingImportFromExtractionCompletedStreamHandler(
    ServiceDbContext dbContext,
    ILogger<PricingImportFromExtractionCompletedStreamHandler> logger
) : IRedisStreamMessageHandler
{
    public string MessageType => AsyncEmailMessageTypes.PricingCompleted;

    public async Task HandleAsync(
        RedisStreamEnvelope envelope,
        CancellationToken cancellationToken = default
    )
    {
        var integrationEvent =
            AsyncEmailStreamPayloadReader.Read<PricingImportFromExtractionCompletedIntegrationEvent>(
                envelope
            );
        var job = await dbContext.EmailExtractionJobs.FirstOrDefaultAsync(
            item =>
                item.Id == integrationEvent.EmailExtractionJobId
                && !item.IsDeleted,
            cancellationToken
        ) ?? throw new InvalidOperationException(
            "No se encontró el trabajo asociado al resultado de Pricing."
        );

        if (job.Status == EmailExtractionJobStatus.SentToPricing)
        {
            return;
        }

        if (job.PricingRequestId != integrationEvent.RequestId)
        {
            throw new InvalidOperationException(
                "El resultado de Pricing no corresponde a la solicitud activa."
            );
        }

        job.MarkSentToPricing(
            integrationEvent.ExtractionExecutionId,
            integrationEvent.PricingImportBatchId,
            job.ConfidenceScore ?? 0m
        );
        if (job.EmailAttachmentId.HasValue)
        {
            var attachment = await dbContext.EmailAttachments.FirstOrDefaultAsync(
                item =>
                    item.Id == job.EmailAttachmentId.Value && !item.IsDeleted,
                cancellationToken
            );
            attachment?.MarkExtracted();
        }

        // Un mismo correo puede traer la matriz tarifaria real junto con PDFs de
        // cargos locales u otros adjuntos complementarios. Esos documentos a veces
        // dejan una extracción determinística con filas parciales y terminan en
        // NeedsReview por faltar POL, equipo, naviera, vigencia y monto. Cuando una
        // matriz hermana ya llegó correctamente a Pricing, ese resultado incompleto
        // no debe mantener todo el correo en rojo ni pedir una revisión falsa.
        var redundantClosedSiblings = await dbContext.EmailExtractionJobs
            .Where(item =>
                item.EmailMessageId == job.EmailMessageId
                && item.Id != job.Id
                && !item.IsDeleted
                && (item.Status == EmailExtractionJobStatus.NeedsReview
                    || item.Status == EmailExtractionJobStatus.Failed)
            )
            .ToListAsync(cancellationToken);

        var archivedSiblingCount = 0;
        foreach (var sibling in redundantClosedSiblings)
        {
            if (!RedundantEmailJobReviewPolicy.IsRedundantAfterPricingSuccess(sibling))
            {
                continue;
            }

            sibling.MarkIgnored(
                "El contenido no produjo una tarifa adicional utilizable y se archivó porque otro contenido del mismo correo ya fue enviado correctamente a Pricing."
            );
            archivedSiblingCount++;
        }

        await EmailJobStateCoordinator.RecalculateAsync(
            dbContext,
            job.EmailMessageId,
            cancellationToken
        );
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Pricing completó el trabajo {EmailExtractionJobId}. "
                + "Solicitud {PricingRequestId}; lote {PricingImportBatchId}; "
                + "filas persistidas {PersistedRows}; omitidas {SkippedRows}; "
                + "revisiones redundantes archivadas {ArchivedSiblingCount}; "
                + "CorrelationId {CorrelationId}.",
            job.Id,
            integrationEvent.RequestId,
            integrationEvent.PricingImportBatchId,
            integrationEvent.PersistedRows,
            integrationEvent.SkippedRows,
            archivedSiblingCount,
            integrationEvent.CorrelationId
        );
    }
}
