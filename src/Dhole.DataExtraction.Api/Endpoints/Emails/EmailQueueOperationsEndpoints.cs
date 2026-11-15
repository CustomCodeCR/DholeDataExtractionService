using System.Data;
using Dhole.DataExtraction.Api.Extensions;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Api.Endpoints.Emails;

/// <summary>
/// Read and administer durable email extraction jobs without manipulating Redis messages
/// or bypassing the idempotent Pricing handoff. Operators must have AI operations scopes.
/// </summary>
public static class EmailQueueOperationsEndpoints
{
    public static IEndpointRouteBuilder MapEmailQueueOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/data-extraction/email/queue")
            .WithTags("Email Queue Operations")
            .RequireAuthorization();

        group.MapGet("/overview", OverviewAsync);
        group.MapGet("/jobs", BrowseAsync);
        group.MapPost("/jobs/{jobId:guid}/retry", RetryAsync);
        return app;
    }

    private static async Task<IResult> OverviewAsync(
        HttpContext http,
        ServiceDbContext db,
        CancellationToken cancellationToken)
    {
        if (!HasScope(http, "ai.execution.view"))
            return Results.Forbid();

        var groups = await db.EmailExtractionJobs.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var counts = groups.ToDictionary(g => g.Status.ToString(), g => g.Count);
        var now = DateTime.UtcNow;
        var stuck = await db.EmailExtractionJobs.AsNoTracking()
            .CountAsync(x => !x.IsDeleted
                && x.Status == EmailExtractionJobStatus.Extracting
                && x.LeaseExpiresAtUtc.HasValue && x.LeaseExpiresAtUtc.Value < now,
                cancellationToken);
        var oldestPending = await db.EmailExtractionJobs.AsNoTracking()
            .Where(x => !x.IsDeleted && x.Status == EmailExtractionJobStatus.Pending)
            .MinAsync(x => (DateTime?)x.CreatedAtUtc, cancellationToken);
        return Results.Ok(new
        {
            generatedAtUtc = now,
            total = counts.Values.Sum(),
            counts,
            stuckLeases = stuck,
            oldestPendingUtc = oldestPending,
        });
    }

    private static async Task<IResult> BrowseAsync(
        HttpContext http,
        int? pageNumber,
        int? pageSize,
        EmailExtractionJobStatus? status,
        string? search,
        bool? stalled,
        ServiceDbContext db,
        CancellationToken cancellationToken)
    {
        if (!HasScope(http, "ai.execution.view"))
            return Results.Forbid();

        var page = Math.Max(pageNumber ?? 1, 1);
        var size = Math.Clamp(pageSize ?? 25, 1, 100);
        var now = DateTime.UtcNow;
        var query =
            from job in db.EmailExtractionJobs.AsNoTracking()
            join email in db.EmailMessages.AsNoTracking()
                on job.EmailMessageId equals email.Id
            where !job.IsDeleted && !email.IsDeleted
            select new { job, email };

        if (status.HasValue)
            query = query.Where(x => x.job.Status == status.Value);

        if (stalled == true)
            query = query.Where(x =>
                x.job.Status == EmailExtractionJobStatus.Extracting
                && x.job.LeaseExpiresAtUtc.HasValue
                && x.job.LeaseExpiresAtUtc.Value < now);

        var term = search?.Trim();
        if (!string.IsNullOrWhiteSpace(term))
        {
            if (Guid.TryParse(term, out var id))
                query = query.Where(x =>
                    x.job.Id == id || x.job.EmailMessageId == id
                    || x.job.AiRequestId == id || x.job.PricingImportBatchId == id);
            else
            {
                var word = term.ToLowerInvariant();
                query = query.Where(x =>
                    x.email.Subject.ToLower().Contains(word)
                    || x.email.FromAddress.ToLower().Contains(word));
            }
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(x => x.job.CreatedAtUtc)
            .ThenByDescending(x => x.job.Id)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(x => new
            {
                id = x.job.Id,
                x.job.EmailMessageId,
                x.job.EmailAttachmentId,
                x.job.SourceType,
                subject = x.email.Subject,
                sender = x.email.FromAddress,
                receivedAt = x.email.ReceivedAt,
                status = x.job.Status.ToString(),
                x.job.AttemptCount,
                x.job.ConfidenceScore,
                x.job.NextAttemptAtUtc,
                x.job.LeaseOwner,
                x.job.LeaseExpiresAtUtc,
                x.job.LastHeartbeatAtUtc,
                x.job.AiRequestId,
                x.job.AiExecutionId,
                x.job.ExtractionExecutionId,
                x.job.PricingRequestId,
                x.job.PricingImportBatchId,
                x.job.LastErrorCode,
                x.job.ErrorMessage,
                x.job.CreatedAtUtc,
                x.job.StartedAt,
                x.job.FinishedAt,
            })
            .ToListAsync(cancellationToken);

        return Results.Ok(new
        {
            generatedAtUtc = now,
            pageNumber = page,
            pageSize = size,
            total,
            items,
        });
    }

    private static async Task<IResult> RetryAsync(
        Guid jobId,
        HttpContext http,
        ServiceDbContext db,
        CancellationToken cancellationToken)
    {
        if (!HasScope(http, "ai.execution.cancel"))
            return Results.Forbid();

        // Serialize competing manual retries, so a failed or reviewed job can only
        // transition once. Workers will claim the Pending job using their lease.
        await using var transaction = await db.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var job = await db.EmailExtractionJobs
            .SingleOrDefaultAsync(x => x.Id == jobId && !x.IsDeleted, cancellationToken);
        if (job is null)
            return Results.NotFound(new { message = "Trabajo de extracción no encontrado." });

        if (job.Status is not EmailExtractionJobStatus.Failed
            and not EmailExtractionJobStatus.NeedsReview)
            return Results.Conflict(new
            {
                message = "Solo se pueden reintentar extracciones fallidas o pendientes de revisión.",
                status = job.Status.ToString(),
            });

        // Never duplicate an existing Pricing operation or a successful sibling job.
        if (job.PricingRequestId.HasValue || job.PricingImportBatchId.HasValue
            || await db.EmailExtractionJobs.AnyAsync(x =>
                x.EmailMessageId == job.EmailMessageId && !x.IsDeleted
                && x.Status == EmailExtractionJobStatus.SentToPricing
                && x.PricingImportBatchId.HasValue, cancellationToken))
            return Results.Conflict(new
            {
                message = "El correo tiene una importación a Pricing registrada. No se permite duplicarla.",
            });

        job.ScheduleAutomaticReprocess(DateTime.UtcNow, http.GetCurrentUserId());
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { jobId, status = job.Status.ToString(), job.AttemptCount });
    }

    private static bool HasScope(HttpContext http, string required) =>
        http.User.Identity?.IsAuthenticated == true
        && http.User.Claims
            .Where(c => c.Type is "scope" or "scp" or "scopes")
            .SelectMany(c => c.Value.Split([' ', ',', ';'],
                StringSplitOptions.RemoveEmptyEntries))
            .Contains(required, StringComparer.OrdinalIgnoreCase);
}
