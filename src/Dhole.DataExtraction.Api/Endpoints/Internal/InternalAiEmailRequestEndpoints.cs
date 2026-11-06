using System.Text.Json;
using Dhole.DataExtraction.Application.Abstractions.Services;
using Dhole.DataExtraction.Domain.Emails;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Api.Endpoints.Internal;

public static class InternalAiEmailRequestEndpoints
{
    private const int MaximumDeterministicRowsForAi = 250;
    private const int MaximumDeterministicIssuesForAi = 80;

    private static readonly JsonSerializerOptions PayloadJsonOptions = new(
        JsonSerializerDefaults.Web
    )
    {
        PropertyNameCaseInsensitive = true,
    };

    public static IEndpointRouteBuilder MapInternalAiEmailRequestEndpoints(
        this IEndpointRouteBuilder app
    )
    {
        var group = app.MapGroup(
            "/api/internal/data-extraction/ai-email-requests"
        ).WithTags("Internal AI Email Requests");

        group.MapGet("/{requestId:guid}", GetRequestAsync);
        group.MapPost("/reconcile", ReconcileRequestsAsync);

        return app;
    }


    // AI asks for lightweight source validity in one batch. Do not return email
    // content here: the caller only needs to know which pending jobs can be closed.
    private sealed record AiEmailReconciliationRequest(Guid[] RequestIds);

    private static async Task<IResult> ReconcileRequestsAsync(
        AiEmailReconciliationRequest payload,
        ServiceDbContext dbContext,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var ids = payload.RequestIds?.Distinct().ToArray();
        if (ids is null || ids.Length == 0 || ids.Length > 250
            || ids.Any(id => id == Guid.Empty))
        {
            return Results.BadRequest(new
            {
                code = "DataExtraction.InvalidAiReconciliationBatch",
                message = "Indique entre 1 y 250 solicitudes válidas."
            });
        }

        var requests = await dbContext.EmailAiAnalysisRequests
            .AsNoTracking()
            .Where(item => ids.Contains(item.Id))
            .Select(item => new
            {
                item.Id,
                item.EmailMessageId,
                item.CompletedAtUtc
            })
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        var messageIds = requests.Values.Select(item => item.EmailMessageId)
            .Distinct().ToArray();
        var messages = await dbContext.EmailMessages
            .AsNoTracking()
            .Where(item => messageIds.Contains(item.Id))
            .Select(item => new
            {
                item.Id,
                item.ReceivedAt,
                item.IsDeleted
            })
            .ToDictionaryAsync(item => item.Id, cancellationToken);

        // The API and the ingestion worker have separate configuration files.
        // Default to archiving, just like the worker's production policy,
        // unless the operator explicitly disables it.
        var closeHistorical = !bool.TryParse(
            configuration["EmailIngestion:ArchivePreviousMonths"], out var enabled)
            || enabled;
        var now = DateTime.UtcNow;
        var items = ids.Select(requestId =>
        {
            if (!requests.TryGetValue(requestId, out var request))
                return new { requestId, shouldCancel = true, reason = "RequestMissing" };

            if (!messages.TryGetValue(request.EmailMessageId, out var message)
                || message.IsDeleted)
                return new { requestId, shouldCancel = true, reason = "SourceDeleted" };

            if (closeHistorical
                && EmailHistoricalMonthPolicy.ShouldArchive(message.ReceivedAt, now))
                return new { requestId, shouldCancel = true, reason = "HistoricalMonth" };

            if (request.CompletedAtUtc.HasValue)
                return new { requestId, shouldCancel = true, reason = "RequestClosed" };

            return new { requestId, shouldCancel = false, reason = "Active" };
        }).ToArray();

        return Results.Ok(new { items });
    }

    private static async Task<IResult> GetRequestAsync(
        Guid requestId,
        ServiceDbContext dbContext,
        IConfiguration configuration,
        CancellationToken cancellationToken
    )
    {
        var request = await dbContext.EmailAiAnalysisRequests
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == requestId, cancellationToken);
        if (request is null)
        {
            return Results.NotFound(
                new
                {
                    code = "DataExtraction.AiEmailRequestNotFound",
                    message = "No se encontró la solicitud interna de AI.",
                }
            );
        }

        // A queued AI request can have been created in November for an October
        // email. Reject it BEFORE returning the source: the AI worker can close
        // the old job without invoking Ollama.
        var sourceReceivedAt = await dbContext.EmailMessages
            .AsNoTracking()
            .Where(message => message.Id == request.EmailMessageId && !message.IsDeleted)
            .Select(message => (DateTime?)message.ReceivedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (!sourceReceivedAt.HasValue)
        {
            return Results.NotFound(new
            {
                code = "DataExtraction.EmailSourceUnavailable",
                message = "El correo original no existe o fue eliminado."
            });
        }

        if ((!bool.TryParse(configuration["EmailIngestion:ArchivePreviousMonths"], out var archiveOld)
                || archiveOld)
            && EmailHistoricalMonthPolicy.ShouldArchive(sourceReceivedAt.Value, DateTime.UtcNow))
        {
            return Results.Json(
                new { code = "DataExtraction.HistoricalEmailClosed",
                    message = "Se archivó la extracción del mes anterior; el documento permanece disponible." },
                statusCode: StatusCodes.Status410Gone);
        }

        if (request.CompletedAtUtc.HasValue)
        {
            return Results.Conflict(
                new
                {
                    code = "DataExtraction.AiEmailRequestCompleted",
                    message = "La solicitud de AI ya no está activa.",
                }
            );
        }

        var maximumPayloadCharacters = ReadPositiveInt(
            configuration["InternalServices:MaximumPayloadCharacters"],
            1_000_000
        );
        if (request.PayloadJson.Length > maximumPayloadCharacters)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "DataExtraction.AiEmailPayloadTooLarge",
                detail: "El payload preparado supera el límite interno configurado."
            );
        }

        AiPricingEmailAnalysisRequest? payload;
        try
        {
            payload = JsonSerializer.Deserialize<AiPricingEmailAnalysisRequest>(
                request.PayloadJson,
                PayloadJsonOptions
            );
        }
        catch (JsonException)
        {
            payload = null;
        }

        if (payload is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status500InternalServerError,
                title: "DataExtraction.InvalidAiEmailPayload",
                detail: "El payload interno de AI no pudo deserializarse."
            );
        }

        // DataExtraction siempre corre primero y conserva su matriz como borrador.
        // AI recibe esa matriz además del contenido original para corregirla/completarla,
        // en vez de volver a interpretar PDF/CSV/XLSX desde cero y poder perder filas.
        if (request.ExtractionExecutionId.HasValue)
        {
            var executionId = request.ExtractionExecutionId.Value;
            var deterministicRecords = await dbContext.PricingExtractionRecords
                .AsNoTracking()
                .Where(item =>
                    item.ExtractionExecutionId == executionId
                    && !item.IsDeleted
                )
                .OrderBy(item => item.SourceSheetName)
                .ThenBy(item => item.SourceRowNumber)
                .Take(MaximumDeterministicRowsForAi)
                .ToListAsync(cancellationToken);

            if (deterministicRecords.Count > 0)
            {
                var deterministicIssues = await dbContext.ExtractionIssues
                    .AsNoTracking()
                    .Where(item =>
                        item.ExtractionExecutionId == executionId
                        && !item.IsDeleted
                    )
                    .OrderByDescending(item => item.IsBlocking)
                    .ThenBy(item => item.SourceSheetName)
                    .ThenBy(item => item.SourceRowNumber)
                    .Take(MaximumDeterministicIssuesForAi)
                    .ToListAsync(cancellationToken);

                payload = payload with
                {
                    PreviousRows = deterministicRecords
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
                    PreviousIssues = deterministicIssues
                        .Select(item => new AiPreviousExtractionIssue(
                            item.Code,
                            item.Message,
                            item.IsBlocking,
                            item.ColumnName,
                            item.RawValue
                        ))
                        .ToArray(),
                };
            }
        }

        payload = ApplySourceIsolation(payload);

        var profileKey = configuration["AI:EmailFallback:ProfileKey"];
        if (string.IsNullOrWhiteSpace(profileKey))
        {
            profileKey = "pricing-email-analysis";
        }

        return Results.Ok(
            new
            {
                requestId = request.Id,
                emailExtractionJobId = request.EmailExtractionJobId,
                emailMessageId = request.EmailMessageId,
                emailAttachmentId = request.EmailAttachmentId,
                requestHash = request.RequestHash,
                correlationId = request.CorrelationId,
                profileKey,
                payload,
                image = new
                {
                    available = false,
                    contentType = (string?)null,
                    downloadUrl = (string?)null,
                },
            }
        );
    }

    private static AiPricingEmailAnalysisRequest ApplySourceIsolation(
        AiPricingEmailAnalysisRequest payload
    )
    {
        var bodyText = string.IsNullOrWhiteSpace(payload.BodyText)
            ? payload.BodyText
            : WrapSection(
                "CURRENT_EMAIL_BODY",
                "El remitente y el asunto vienen del envelope del correo. No sustituya esos datos con firmas, encabezados citados o adjuntos.",
                payload.BodyText!
            );

        var sourceName = SanitizeHeader(payload.SourceName);
        var sourceType = SanitizeHeader(payload.SourceType);
        var contentType = SanitizeHeader(payload.SourceContentType ?? "unknown");
        var sourceContent = payload.SourceContent ?? string.Empty;

        if (!sourceContent.Contains("[[ATTACHMENT_SOURCE_BEGIN]]", StringComparison.Ordinal))
        {
            sourceContent = $"""
                [[ATTACHMENT_SOURCE_BEGIN]]
                source_name: {sourceName}
                source_type: {sourceType}
                content_type: {contentType}
                isolation_rules:
                - Todo dato entre BEGIN/END pertenece únicamente a esta fuente.
                - No copie POL, POE, POD, naviera, equipo, fechas, moneda ni montos desde otro adjunto o desde el historial del correo para completar huecos de esta fuente.
                - Si dos fuentes discrepan, conserve el valor de su propia fuente y deje el campo incierto vacío antes de mezclar datos.
                - PreviousRows es una referencia determinística para corregir/completar, no autorización para combinar filas de documentos diferentes.
                [[ATTACHMENT_CONTENT_BEGIN]]
                {sourceContent}
                [[ATTACHMENT_CONTENT_END]]
                [[ATTACHMENT_SOURCE_END]]
                """;
        }

        return payload with
        {
            BodyText = bodyText,
            SourceContent = sourceContent,
        };
    }

    private static string WrapSection(string name, string rule, string content)
    {
        if (content.Contains($"[[{name}_BEGIN]]", StringComparison.Ordinal))
            return content;

        return $"""
            [[{name}_BEGIN]]
            rule: {rule}
            {content}
            [[{name}_END]]
            """;
    }

    private static string SanitizeHeader(string value)
        => value.Replace('\r', ' ').Replace('\n', ' ').Trim();

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }
}
