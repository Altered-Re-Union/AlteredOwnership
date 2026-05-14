using System.Text.Json;
using AlteredOwnership.Server.Cards;
using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Events;
using Microsoft.EntityFrameworkCore;

namespace AlteredOwnership.Server.Services;

public class CollectionWriter(OwnershipDbContext db, TimeProvider time)
{
    public async Task ImportAsync(Guid userId, EquinoxImportEvent.PayloadV1 payload, CancellationToken ct)
    {
        // Aspire's Npgsql integration enables retrying execution strategy, so the
        // transaction has to be opened inside ExecuteAsync. Clear the change tracker
        // at each attempt so a transient-failure retry starts from a clean slate.
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var existingEvents = await LoadUserEventsAsync(userId, ct);
            var newEvent = BuildEvent(userId, EquinoxImportEvent.Kind, payload, NextUserEventId(existingEvents));
            db.OwnershipEvents.Add(newEvent);

            var expectedState = EventReplay.ReplayAll(existingEvents.Append(newEvent));
            var currentRows = await LoadCurrentProjectionAsync(userId, ct);
            ReconcileProjection(userId, currentRows, expectedState);

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        });
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
