using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AlteredOwnership.Server.Infrastructure.EventSourcing;

public class DuplicateImportException() : Exception("This export has already been imported.");

public class EventAppender(OwnershipDbContext db, TimeProvider time)
{
    // Caller fills UserId, Kind, Payload, PayloadHash. The appender assigns
    // UserEventId (per-user monotonic), CreatedAt, persists, and runs the caller's
    // projection reconciliation inside the same transaction.
    public async Task AppendAsync(
        OwnershipEvent newEvent,
        Func<Dictionary<string, int>, CancellationToken, Task> reconcileProjectionAsync,
        CancellationToken ct)
    {
        // Aspire's Npgsql integration enables retrying execution strategy, so the
        // transaction has to be opened inside ExecuteAsync. Clear the change tracker
        // at each attempt so a transient-failure retry starts from a clean slate.
        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(ct);

                var existingEvents = await LoadUserEventsAsync(newEvent.UserId, ct);
                newEvent.UserEventId = existingEvents.Count == 0 ? 1 : existingEvents[^1].UserEventId + 1;
                newEvent.CreatedAt = time.GetUtcNow();
                db.OwnershipEvents.Add(newEvent);

                var expectedState = EventReplay.ReplayAll(existingEvents.Append(newEvent));
                await reconcileProjectionAsync(expectedState, ct);

                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: "23505", ConstraintName: "IX_OwnershipEvents_PayloadHash" })
        {
            throw new DuplicateImportException();
        }
    }

    private Task<List<OwnershipEvent>> LoadUserEventsAsync(Guid userId, CancellationToken ct) =>
        db.OwnershipEvents
            .Where(e => e.UserId == userId)
            .OrderBy(e => e.UserEventId)
            .ToListAsync(ct);
}
