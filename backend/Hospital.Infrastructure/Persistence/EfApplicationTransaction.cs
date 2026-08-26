using Hospital.Core.Persistence;

using Microsoft.EntityFrameworkCore.Storage;

namespace Hospital.Infrastructure.Persistence;

internal sealed class EfApplicationTransaction(ApplicationDbContext dbContext) : IApplicationTransaction
{
    public async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using IDbContextTransaction transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        T result = await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
