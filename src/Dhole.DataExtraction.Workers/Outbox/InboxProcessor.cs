using CustomCodeFramework.Messaging.Inbox;
using CustomCodeFramework.Messaging.Outbox.Processing;
using Dhole.DataExtraction.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Workers.Outbox;

internal sealed class InboxProcessor(ServiceDbContext dbContext) : IInboxProcessor
{
    public async Task<InboxProcessingResult> CleanupAsync(
        TimeSpan olderThan,
        int batchSize,
        CancellationToken cancellationToken = default
    )
    {
        var limitDate = DateTime.UtcNow.Subtract(olderThan);

        var messages = await dbContext
            .InboxMessages.Where(x =>
                x.Status == InboxMessageStatus.Processed
                && x.ProcessedAtUtc != null
                && x.ProcessedAtUtc < limitDate
            )
            .OrderBy(x => x.ProcessedAtUtc)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (messages.Count == 0)
        {
            return InboxProcessingResult.Empty;
        }

        dbContext.InboxMessages.RemoveRange(messages);
        await dbContext.SaveChangesAsync(cancellationToken);

        var hasMoreMessages = await dbContext.InboxMessages.AnyAsync(
            x => x.Status == InboxMessageStatus.Processed
                 && x.ProcessedAtUtc != null
                 && x.ProcessedAtUtc < limitDate,
            cancellationToken
        );

        return new InboxProcessingResult(messages.Count, hasMoreMessages);
    }
}
