using System.Text.Json;
using AlteredOwnership.Server.Cards;
using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Events;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AlteredOwnership.Server.Services;

public class DuplicateImportException() : Exception("This export has already been imported.");

public class DuplicateUniquesException(IReadOnlyList<string> references)
    : Exception("One or more uniques in the import are already owned.")
{
    public IReadOnlyList<string> References { get; } = references;
}

public class CollectionWriter(OwnershipDbContext db, TimeProvider time)
{
    public async Task ImportAsync(Guid userId, EquinoxImportEvent.PayloadV1 payload, CancellationToken ct)
    {
        // Aspire's Npgsql integration enables retrying execution strategy, so the
        // transaction has to be opened inside ExecuteAsync. Clear the change tracker
        // at each attempt so a transient-failure retry starts from a clean slate.
        // We run the happy path optimistically and diagnose conflicts only after the
        // DB constraints fire — no wasted work or race windows on the common case.
        var strategy = db.Database.CreateExecutionStrategy();
        try
        {
            await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(ct);

                var existingEvents = await LoadUserEventsAsync(userId, ct);
                var newEvent = BuildEvent(userId, EquinoxImportEvent.Kind, payload, NextUserEventId(existingEvents));
                newEvent.PayloadHash = EquinoxImportEvent.ComputeHash(payload);
                db.OwnershipEvents.Add(newEvent);

                var expectedState = EventReplay.ReplayAll(existingEvents.Append(newEvent));
                var currentRows = await LoadCurrentProjectionAsync(userId, ct);
                ReconcileProjection(userId, currentRows, expectedState);

                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            });
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: "23505", ConstraintName: "IX_OwnershipEvents_PayloadHash" })
        {
            throw new DuplicateImportException();
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: "23514", ConstraintName: "CK_CardOwnerships_UniqueQuantityOne" })
        {
            db.ChangeTracker.Clear();
            var newUniqueRefs = payload.Cards
                .Where(c => CardReferenceParser.IsUnique(c.Reference))
                .Select(c => c.Reference)
                .ToHashSet();
            var alreadyOwned = await db.CardOwnerships
                .Where(c => c.UserId == userId && newUniqueRefs.Contains(c.CardReference))
                .Select(c => c.CardReference)
                .ToListAsync(ct);
            throw new DuplicateUniquesException(alreadyOwned);
        }
    }

    private Task<List<OwnershipEvent>> LoadUserEventsAsync(Guid userId, CancellationToken ct) =>
        db.OwnershipEvents
            .Where(e => e.UserId == userId)
            .OrderBy(e => e.UserEventId)
            .ToListAsync(ct);

    private static int NextUserEventId(IReadOnlyList<OwnershipEvent> events) =>
        events.Count == 0 ? 1 : events[^1].UserEventId + 1;

    private OwnershipEvent BuildEvent(Guid userId, EventKind kind, object payload, int userEventId) =>
        new()
        {
            UserId = userId,
            UserEventId = userEventId,
            Kind = kind,
            Payload = JsonSerializer.SerializeToDocument(payload),
            CreatedAt = time.GetUtcNow(),
        };

    private Task<Dictionary<string, CardOwnership>> LoadCurrentProjectionAsync(Guid userId, CancellationToken ct) =>
        db.CardOwnerships
            .Where(c => c.UserId == userId)
            .ToDictionaryAsync(c => c.CardReference, ct);

    private void ReconcileProjection(
        Guid userId,
        Dictionary<string, CardOwnership> currentRows,
        Dictionary<string, int> expectedState)
    {
        foreach (var (reference, quantity) in expectedState)
        {
            if (currentRows.Remove(reference, out var row))
            {
                row.Quantity = quantity;
            }
            else
            {
                db.CardOwnerships.Add(new CardOwnership
                {
                    UserId = userId,
                    CardReference = reference,
                    Quantity = quantity,
                    IsUnique = CardReferenceParser.IsUnique(reference),
                });
            }
        }

        foreach (var orphan in currentRows.Values)
            db.CardOwnerships.Remove(orphan);
    }
}
