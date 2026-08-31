using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Infrastructure.EventSourcing;
using Microsoft.EntityFrameworkCore;

namespace AlteredOwnership.Server.Domain.Services;

// Keeps a user's alt-art preferences in line whenever their card ownership can shrink.
// If a preferred printing's owned quantity drops below how many slots currently point
// to it, the excess slots are cleared so they fall back to the group's default art
// instead of pointing at copies the player no longer has.
//
// Wired into CollectionImporter.ReconcileCardOwnershipsAsync, the only reconciliation
// call site today — but EquinoxImportEvent.ApplyV1 sums every import's quantities
// rather than replacing them (each Equinox export is a delta of newly-owned cards, not
// a full snapshot), so `expected.Cards` is currently monotonically non-decreasing and
// this never actually trims anything in production yet. It's still correct and wired
// up for whenever a real decrease enters the projection (a trade/loss event, a
// corrected import, etc.) — see AltArtEndpointsTests for a test that exercises the
// logic directly rather than through the (currently increase-only) import endpoint.
public class AltArtPreferenceReconciler(OwnershipDbContext db)
{
    public async Task ReconcileAsync(Guid userId, ProjectionState expected, CancellationToken ct)
    {
        var preferences = await db.UserCardArtPreferences
            .Where(p => p.UserId == userId)
            .ToListAsync(ct);
        if (preferences.Count == 0)
            return;

        var refs = preferences.Select(p => p.PreferredReference).Distinct().ToList();
        var catalogByRef = await db.CardArtCatalog
            .Where(c => refs.Contains(c.Reference))
            .ToDictionaryAsync(c => c.Reference, ct);

        bool IsInfinite(string reference) =>
            catalogByRef.TryGetValue(reference, out var catalogEntry)
                && AltArtRules.IsInfinite(catalogEntry);

        foreach (var group in preferences.GroupBy(p => (p.FamilyId, p.Faction, p.Rarity)))
        {
            var usedSoFar = new Dictionary<string, int>();
            foreach (var pref in group.OrderBy(p => p.SlotIndex))
            {
                if (IsInfinite(pref.PreferredReference))
                    continue;

                var ownedQuantity = expected.Cards.GetValueOrDefault(pref.PreferredReference, 0);
                var used = usedSoFar.GetValueOrDefault(pref.PreferredReference, 0);
                if (used + 1 > ownedQuantity)
                    db.UserCardArtPreferences.Remove(pref);
                else
                    usedSoFar[pref.PreferredReference] = used + 1;
            }
        }
    }
}
