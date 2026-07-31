using System.Text.Json;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Api.Endpoints.Internal;

public static class InternalAiEmailRequestEndpoints
{
    public static IEndpointRouteBuilder MapInternalAiEmailRequestEndpoints(
        this IEndpointRouteBuilder app
    )
    {
        var group = app.MapGroup(
            "/api/internal/data-extraction/ai-email-requests"
        ).WithTags("Internal AI Email Requests");

        group.MapGet("/{requestId:guid}", GetRequestAsync);

        return app;
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

        using var document = JsonDocument.Parse(request.PayloadJson);
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
                payload = document.RootElement.Clone(),
                image = new
                {
                    available = false,
                    contentType = (string?)null,
                    downloadUrl = (string?)null,
                },
            }
        );
    }

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }
}
