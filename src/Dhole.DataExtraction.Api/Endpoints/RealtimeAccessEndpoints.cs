using Dhole.DataExtraction.Api.Authorization;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Api.Endpoints;

/// <summary>
/// F6.08: owner-authorized existence checks only, with no email, file content,
/// AI request payload, contact metadata or inferred recipient in response.
/// These are NEVER public endpoints and must not accept anonymous requests.
/// </summary>
public static class RealtimeAccessEndpoints
{
    public static IEndpointRouteBuilder MapRealtimeAccessEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/data-extraction/realtime-access")
            .WithTags("Realtime Access")
            .RequireAuthorization();

        group.MapGet("/execution/{id:guid}", async (Guid id, ServiceDbContext db,
            CancellationToken ct) =>
            await db.ExtractionExecutions.AsNoTracking()
                .AnyAsync(x => x.Id == id && !x.IsDeleted, ct)
                ? Results.Ok() : Results.NotFound())
            .RequireScope(DataExtractorScopeNames.EmailExtractionsView);

        group.MapGet("/emailjob/{id:guid}", async (Guid id, ServiceDbContext db,
            CancellationToken ct) =>
            await db.EmailExtractionJobs.AsNoTracking()
                .AnyAsync(x => x.Id == id && !x.IsDeleted, ct)
                ? Results.Ok() : Results.NotFound())
            .RequireScope(DataExtractorScopeNames.EmailExtractionsView);

        group.MapGet("/airequest/{id:guid}", async (Guid id, ServiceDbContext db,
            CancellationToken ct) =>
            await db.EmailAiAnalysisRequests.AsNoTracking()
                .AnyAsync(x => x.Id == id, ct)
                ? Results.Ok() : Results.NotFound())
            .RequireScope(DataExtractorScopeNames.EmailExtractionsView);
        return app;
    }
}
