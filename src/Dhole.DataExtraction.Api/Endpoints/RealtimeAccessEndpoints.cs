using Dhole.DataExtraction.Api.Authorization;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Api.Endpoints;

/// <summary>
/// F6.08: authenticated minimal resource snapshots for REST recovery. Never
/// expose mail, file payload, sender, extracted values, AI prompt or hash.
/// The Gateway reuses these routes for owner scope checks.
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
        {
            var snapshot = await db.ExtractionExecutions.AsNoTracking()
                .Where(x => x.Id == id && !x.IsDeleted)
                .Select(x => new
                {
                    x.Id,
                    x.Status,
                    x.TotalRows,
                    x.ValidRows,
                    x.WarningRows,
                    x.InvalidRows,
                    x.StartedAt,
                    x.CompletedAt,
                    x.FailedAt
                }).FirstOrDefaultAsync(ct);
            return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
        }).RequireScope(DataExtractorScopeNames.EmailExtractionsView);

        group.MapGet("/emailjob/{id:guid}", async (Guid id, ServiceDbContext db,
            CancellationToken ct) =>
        {
            var snapshot = await db.EmailExtractionJobs.AsNoTracking()
                .Where(x => x.Id == id && !x.IsDeleted)
                .Select(x => new
                {
                    x.Id,
                    x.Status,
                    x.Version,
                    x.AttemptCount,
                    x.NextAttemptAtUtc,
                    x.StartedAt,
                    x.FinishedAt
                }).FirstOrDefaultAsync(ct);
            return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
        }).RequireScope(DataExtractorScopeNames.EmailExtractionsView);

        group.MapGet("/airequest/{id:guid}", async (Guid id, ServiceDbContext db,
            CancellationToken ct) =>
        {
            // Some requests legitimately reopen after a lost lease; the
            // completion timestamp is authoritative, not the SignalR hint.
            var snapshot = await db.EmailAiAnalysisRequests.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new
                {
                    x.Id,
                    x.EmailExtractionJobId,
                    x.CompletedAtUtc
                }).FirstOrDefaultAsync(ct);
            return snapshot is null ? Results.NotFound() : Results.Ok(snapshot);
        }).RequireScope(DataExtractorScopeNames.EmailExtractionsView);
        return app;
    }
}
