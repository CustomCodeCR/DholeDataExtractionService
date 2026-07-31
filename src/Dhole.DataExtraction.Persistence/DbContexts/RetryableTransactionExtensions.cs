using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Dhole.DataExtraction.Persistence.DbContexts;

public static class RetryableTransactionExtensions
{
    public static async Task ExecuteInRetryableTransactionAsync(
        this DbContext dbContext,
        Func<Task> operation,
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken
    )
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                isolationLevel,
                cancellationToken
            );
            await operation();
            await transaction.CommitAsync(cancellationToken);
        });
    }

    public static async Task<TResult> ExecuteInRetryableTransactionAsync<TResult>(
        this DbContext dbContext,
        Func<Task<TResult>> operation,
        IsolationLevel isolationLevel,
        CancellationToken cancellationToken
    )
    {
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                isolationLevel,
                cancellationToken
            );
            var result = await operation();
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }
}
