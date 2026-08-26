using Hospital.Core.Audit;
using Hospital.Core.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hospital.Api.IntegrationTests;

[Collection(AuthenticationDatabaseTestGroup.Name)]
public sealed class ApplicationTransactionTests(AuthenticationDatabaseFixture database)
{
    [Fact]
    public async Task ExceptionAfterSaveRollsBackTheWholeOperation()
    {
        string action = $"RollbackTest{Guid.NewGuid():N}";
        using AuthenticationApiFactory factory = new(database.ConnectionString);
        using IServiceScope scope = factory.Services.CreateScope();
        IApplicationDbContext context = scope.ServiceProvider
            .GetRequiredService<IApplicationDbContext>();
        IApplicationTransaction transaction = scope.ServiceProvider
            .GetRequiredService<IApplicationTransaction>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            transaction.ExecuteAsync<int>(async cancellationToken =>
            {
                context.AuditEvents.Add(new AuditEvent
                {
                    Action = action,
                    AffectedEntityType = "TransactionTest",
                    OccurredAtUtc = AuthTestClock.UtcNow,
                });
                await context.SaveChangesAsync(cancellationToken);
                throw new InvalidOperationException("Force transaction rollback.");
            }));

        await using var verificationContext = database.CreateContext();
        Assert.False(await verificationContext.AuditEvents
            .AsNoTracking()
            .AnyAsync(auditEvent => auditEvent.Action == action));
    }
}
