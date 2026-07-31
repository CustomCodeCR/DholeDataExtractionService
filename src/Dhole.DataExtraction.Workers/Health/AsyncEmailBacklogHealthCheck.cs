using CustomCodeFramework.Messaging.Inbox;
using CustomCodeFramework.Messaging.Outbox;
using Dhole.DataExtraction.Domain.Emails.Enums;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Dhole.DataExtraction.Workers.Health;

internal sealed class AsyncEmailBacklogHealthCheck(
    ServiceDbContext dbContext,
    IConfiguration configuration
) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        var statuses = await dbContext.EmailExtractionJobs
            .AsNoTracking()
            .Where(job => !job.IsDeleted)
            .GroupBy(job => job.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToDictionaryAsync(
                item => item.Status,
                item => item.Count,
                cancellationToken
            );
        var now = DateTime.UtcNow;
        var staleJobs = await dbContext.EmailExtractionJobs
            .AsNoTracking()
            .CountAsync(
                job =>
                    !job.IsDeleted
                    && job.Status == EmailExtractionJobStatus.Extracting
                    && job.LeaseExpiresAtUtc.HasValue
                    && job.LeaseExpiresAtUtc.Value < now,
                cancellationToken
            );
        var outboxPending = await dbContext.OutboxMessages.CountAsync(
            message => message.Status == OutboxMessageStatus.Pending,
            cancellationToken
        );
        var outboxFailed = await dbContext.OutboxMessages.CountAsync(
            message => message.Status == OutboxMessageStatus.Failed,
            cancellationToken
        );
        var inboxPending = await dbContext.InboxMessages.CountAsync(
            message => message.Status == InboxMessageStatus.Pending,
            cancellationToken
        );
        var inboxFailed = await dbContext.InboxMessages.CountAsync(
            message => message.Status == InboxMessageStatus.Failed,
            cancellationToken
        );
        var activeBacklog =
            Get(statuses, EmailExtractionJobStatus.Pending)
            + Get(statuses, EmailExtractionJobStatus.AwaitingAi)
            + Get(statuses, EmailExtractionJobStatus.AwaitingPricing);
        var warningThreshold = ReadPositiveInt(
            configuration[
                "Monitoring:AsyncEmail:BacklogWarningThreshold"
            ],
            100
        );
        var data = new Dictionary<string, object>
        {
            ["email_jobs_pending"] = Get(
                statuses,
                EmailExtractionJobStatus.Pending
            ),
            ["email_jobs_awaiting_ai"] = Get(
                statuses,
                EmailExtractionJobStatus.AwaitingAi
            ),
            ["email_jobs_awaiting_pricing"] = Get(
                statuses,
                EmailExtractionJobStatus.AwaitingPricing
            ),
            ["email_jobs_stale"] = staleJobs,
            ["outbox_pending"] = outboxPending,
            ["outbox_failed"] = outboxFailed,
            ["inbox_pending"] = inboxPending,
            ["inbox_failed"] = inboxFailed,
        };

        return staleJobs > 0
            || outboxFailed > 0
            || inboxFailed > 0
            || activeBacklog > warningThreshold
            || outboxPending > warningThreshold
            || inboxPending > warningThreshold
            ? HealthCheckResult.Degraded(
                "El flujo asíncrono de correos tiene backlog o trabajos abandonados.",
                data: data
            )
            : HealthCheckResult.Healthy(
                "El flujo asíncrono de correos está operativo.",
                data
            );
    }

    private static int Get(
        IReadOnlyDictionary<EmailExtractionJobStatus, int> values,
        EmailExtractionJobStatus status
    )
    {
        return values.TryGetValue(status, out var count) ? count : 0;
    }

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }
}
