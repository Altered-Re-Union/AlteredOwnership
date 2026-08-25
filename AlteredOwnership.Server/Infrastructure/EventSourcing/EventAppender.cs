using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AlteredOwnership.Server.Infrastructure.EventSourcing;

public class DuplicateImportException() : Exception("This export has already been imported.");

public class EventAppender(OwnershipDbContext db, TimeProvider time)
{
    // Runs `work` inside one retry-aware transaction: either every event it appends
    // through the given EventBatch is durably recorded and every projection
    // reconciled, or — on any exception, including a mid-batch failure — none of them
    // are, since the transaction never commits and rolls back automatically.
    public async Task<TResult> RunInTransactionAsync<TResult>(
        Func<EventBatch, CancellationToken, Task<TResult>> work, CancellationToken ct)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            return await strategy.ExecuteAsync(async () =>
            {
                // Aspire's Npgsql integration enables retrying execution strategy, so the
                // transaction has to be opened inside ExecuteAsync. Clear the change tracker
                // at each attempt so a transient-failure retry starts from a clean slate.
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(ct);

                var batch = new EventBatch(db, time);
                var result = await work(batch, ct);

                await tx.CommitAsync(ct);
                return result;
            });
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: "23505", ConstraintName: "IX_OwnershipEvents_PayloadHash" })
        {
            throw new DuplicateImportException();
        }
    }

    public Task RunInTransactionAsync(Func<EventBatch, CancellationToken, Task> work, CancellationToken ct) =>
        RunInTransactionAsync<object?>(async (batch, c) => { await work(batch, c); return null; }, ct);

    // Convenience for the common single-event case.
    public Task AppendAsync(
        OwnershipEvent newEvent,
        Func<ProjectionState, CancellationToken, Task> reconcileProjectionAsync,
        CancellationToken ct) =>
        RunInTransactionAsync((batch, c) => batch.AppendAsync(newEvent, reconcileProjectionAsync, c), ct);
}

// Appends one or more events — for potentially different users — within a single
// transaction managed by EventAppender.RunInTransactionAsync. Keeps a per-user event
// history in memory so multiple events for the same user in one batch are numbered
// and folded correctly against each other, not just against what's already in the DB.
public class EventBatch(OwnershipDbContext db, TimeProvider time)
{
    private readonly Dictionary<Guid, List<OwnershipEvent>> _historyByUser = new();

    public async Task AppendAsync(
        OwnershipEvent newEvent,
        Func<ProjectionState, CancellationToken, Task> reconcileProjectionAsync,
        CancellationToken ct)
    {
        if (!_historyByUser.TryGetValue(newEvent.UserId, out var history))
        {
            history = await db.OwnershipEvents
                .Where(e => e.UserId == newEvent.UserId)
                .OrderBy(e => e.UserEventId)
                .ToListAsync(ct);
            _historyByUser[newEvent.UserId] = history;
        }

        newEvent.UserEventId = history.Count == 0 ? 1 : history[^1].UserEventId + 1;
        newEvent.CreatedAt = time.GetUtcNow();
        db.OwnershipEvents.Add(newEvent);
        history.Add(newEvent);

        var expectedState = EventReplay.ReplayAll(history);
        await reconcileProjectionAsync(expectedState, ct);

        // Flush within the still-open transaction so a later append in this same batch
        // (including another one for this same user) sees this via a normal query,
        // instead of re-adding an entity already staged in the change tracker.
        await db.SaveChangesAsync(ct);
    }
}
