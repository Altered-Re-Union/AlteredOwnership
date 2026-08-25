using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Domain;
using AlteredOwnership.Server.Infrastructure.EventSourcing;
using Microsoft.EntityFrameworkCore;

namespace AlteredOwnership.Server.Domain.Services;

// Given the full replayed state for a user, brings CardOwnership and
// BoosterInventory in line with it. Shared by every service that appends events
// (RewardService, BoosterService) so both projections are always kept in sync with
// whatever the event history actually says, never patched ad hoc.
public class ProjectionReconciler(OwnershipDbContext db)
{
    public async Task ReconcileAsync(Guid userId, ProjectionState expected, CancellationToken ct)
    {
        await ReconcileCardsAsync(userId, expected.Cards, ct);
        await ReconcileBoostersAsync(userId, expected.Boosters, ct);
    }

    private async Task ReconcileCardsAsync(Guid userId, Dictionary<string, int> expectedState, CancellationToken ct)
    {
        var currentRows = await db.CardOwnerships
            .Where(c => c.UserId == userId)
            .ToDictionaryAsync(c => c.CardReference, ct);

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
                    IsUnique = CardReferenceParser.IsUnique(reference)
                });
            }
        }
    }

    private async Task ReconcileBoostersAsync(Guid userId, Dictionary<string, int> expectedState, CancellationToken ct)
    {
        var currentRows = await db.BoosterInventories
            .Where(b => b.UserId == userId)
            .ToDictionaryAsync(b => b.BoosterTypeKey, ct);

        foreach (var (boosterTypeKey, quantity) in expectedState)
        {
            currentRows.Remove(boosterTypeKey, out var row);

            // Exactly 0 means fully consumed: delete rather than keep a zero row.
            // A negative quantity is invalid (opening more than owned) and must NOT
            // be silently dropped here — it has to reach the DB so the
            // CK_BoosterInventories_QuantityNonNegative constraint rejects it and
            // BoosterService can translate that into NoBoosterAvailableException.
            if (quantity == 0)
            {
                if (row is not null) db.BoosterInventories.Remove(row);
                continue;
            }

            if (row is not null) row.Quantity = quantity;
            else db.BoosterInventories.Add(new BoosterInventory { UserId = userId, BoosterTypeKey = boosterTypeKey, Quantity = quantity });
        }
    }
}
