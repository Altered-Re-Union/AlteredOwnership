using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace AlteredOwnership.Server.Domain.Services;

public class InvalidAltArtRequestException(string message) : Exception(message);

public class AltArtSlotShortfallException(IReadOnlyList<OwnershipShortfall> shortfalls)
    : Exception("Not enough copies owned for the requested slot assignment.")
{
    public IReadOnlyList<OwnershipShortfall> Shortfalls { get; } = shortfalls;
}

public class AltArtService(OwnershipDbContext db)
{
    public async Task<List<AltArtFamilyResponse>> GetFamiliesAsync(AltArtFamilyQuery query, string locale, CancellationToken ct)
    {
        IQueryable<CardArtCatalogEntry> q = db.CardArtCatalog.AsNoTracking();
        if (query.Factions.Count > 0) q = q.Where(c => query.Factions.Contains(c.Faction));
        if (query.Rarities.Count > 0) q = q.Where(c => query.Rarities.Contains(c.Rarity));

        // Grouping + the "> 1 distinct illustration" check need every matching row in
        // memory anyway — the catalog is a few thousand rows total, not worth a more
        // elaborate SQL-side aggregation.
        var rows = await q.ToListAsync(ct);

        var results = new List<AltArtFamilyResponse>();
        foreach (var groupRows in rows.GroupBy(r => (r.FamilyId, r.Faction, r.Rarity)).Select(g => g.ToList()))
        {
            if (groupRows.Select(r => r.Reference).Distinct().Count() <= 1)
                continue;

            var representative = ResolveDefaultRow(groupRows);
            var name = CardLocalization.Localize(representative.FamilyName, locale);

            if (query.Name is not null && !(name?.Contains(query.Name, StringComparison.OrdinalIgnoreCase) ?? false))
                continue;
            if (!MatchesNumeric(representative.MainCost, query.MainCost))
                continue;

            results.Add(new AltArtFamilyResponse(
                representative.FamilyId, representative.Faction, representative.Rarity,
                representative.Reference, name, representative.CardType, representative.MainCost));
        }

        return results;
    }

    public async Task<List<AltArtOptionsResponse>> GetOptionsAsync(
        Guid userId, IReadOnlyList<AltArtGroupKey> keys, CancellationToken ct)
    {
        if (keys.Count == 0)
            return [];

        var familyIds = keys.Select(k => k.FamilyId).Distinct().ToList();
        var keySet = keys.Select(k => (k.FamilyId, k.Faction, k.Rarity)).ToHashSet();

        var rowsByGroup = (await db.CardArtCatalog
                .Where(c => familyIds.Contains(c.FamilyId))
                .AsNoTracking()
                .ToListAsync(ct))
            .Where(r => keySet.Contains((r.FamilyId, r.Faction, r.Rarity)))
            .GroupBy(r => (r.FamilyId, r.Faction, r.Rarity))
            .ToDictionary(g => g.Key, g => g.ToList());

        var refsInScope = rowsByGroup.Values.SelectMany(v => v).Select(r => r.Reference).Distinct().ToList();
        var owned = await db.CardOwnerships
            .Where(o => o.UserId == userId && refsInScope.Contains(o.CardReference))
            .AsNoTracking()
            .ToDictionaryAsync(o => o.CardReference, o => o.Quantity, ct);

        var prefsByGroup = await LoadPreferencesByGroupAsync(userId, ct);

        var results = new List<AltArtOptionsResponse>();
        foreach (var key in keys)
        {
            var groupKey = (key.FamilyId, key.Faction, key.Rarity);
            if (!rowsByGroup.TryGetValue(groupKey, out var groupRows) || groupRows.Count == 0)
                continue;

            var maxSlots = AltArtRules.MaxSlots(groupRows[0].CardType);
            var defaultReference = ResolveDefaultRow(groupRows).Reference;

            // The default print always sorts first regardless of its own SortOrder — a
            // caller-facing "leftmost = standard art" guarantee that plain SortOrder
            // can't provide once a genuinely tracked alt-art print happens to have a
            // lower SortOrder than the group's untracked default (see ResolveDefaultRow).
            var options = groupRows
                .OrderBy(r => r.Reference == defaultReference ? 0 : 1)
                .ThenBy(r => r.SortOrder)
                .Select(r =>
                {
                    var isInfinite = AltArtRules.IsInfinite(r);
                    int? ownedQuantity = isInfinite ? null : owned.GetValueOrDefault(r.Reference, 0);
                    return new AltArtOption(r.Reference, r.Set, r.IsPromo, ownedQuantity, r.SortOrder);
                })
                .ToList();

            prefsByGroup.TryGetValue(groupKey, out var groupPrefs);
            var slots = ResolveSlots(maxSlots, defaultReference, groupPrefs);

            results.Add(new AltArtOptionsResponse(key.FamilyId, key.Faction, key.Rarity, options, slots));
        }

        return results;
    }

    public async Task SetPreferenceAsync(Guid userId, SetAltArtPreferenceRequest request, CancellationToken ct)
    {
        var groupRows = await db.CardArtCatalog
            .Where(c => c.FamilyId == request.FamilyId && c.Faction == request.Faction && c.Rarity == request.Rarity)
            .AsNoTracking()
            .ToListAsync(ct);

        if (groupRows.Count == 0)
            throw new InvalidAltArtRequestException("Unknown card family/faction/rarity group.");

        var maxSlots = AltArtRules.MaxSlots(groupRows[0].CardType);
        if (request.SlotReferences.Count != maxSlots)
            throw new InvalidAltArtRequestException($"This group has {maxSlots} slot(s); expected exactly that many entries.");

        var rowsByRef = groupRows.ToDictionary(r => r.Reference);
        foreach (var reference in request.SlotReferences)
            if (reference is not null && !rowsByRef.ContainsKey(reference))
                throw new InvalidAltArtRequestException($"Reference '{reference}' does not belong to this group.");

        var requestedCounts = new Dictionary<string, int>();
        foreach (var reference in request.SlotReferences)
            if (reference is not null)
                requestedCounts[reference] = requestedCounts.GetValueOrDefault(reference) + 1;

        var owned = await db.CardOwnerships
            .Where(o => o.UserId == userId && requestedCounts.Keys.Contains(o.CardReference))
            .AsNoTracking()
            .ToDictionaryAsync(o => o.CardReference, o => o.Quantity, ct);

        var shortfalls = requestedCounts
            .Where(kv => !AltArtRules.IsInfinite(rowsByRef[kv.Key]))
            .Select(kv => new OwnershipShortfall(kv.Key, kv.Value, owned.GetValueOrDefault(kv.Key, 0)))
            .Where(s => s.Owned < s.Requested)
            .ToList();

        if (shortfalls.Count > 0)
            throw new AltArtSlotShortfallException(shortfalls);

        var existing = await db.UserCardArtPreferences
            .Where(p => p.UserId == userId && p.FamilyId == request.FamilyId
                && p.Faction == request.Faction && p.Rarity == request.Rarity)
            .ToDictionaryAsync(p => p.SlotIndex, ct);

        for (var i = 0; i < request.SlotReferences.Count; i++)
        {
            var slotIndex = i + 1;
            var reference = request.SlotReferences[i];

            if (existing.TryGetValue(slotIndex, out var row))
            {
                if (reference is null) db.UserCardArtPreferences.Remove(row);
                else row.PreferredReference = reference;
            }
            else if (reference is not null)
            {
                db.UserCardArtPreferences.Add(new UserCardArtPreference
                {
                    UserId = userId,
                    FamilyId = request.FamilyId,
                    Faction = request.Faction,
                    Rarity = request.Rarity,
                    SlotIndex = slotIndex,
                    PreferredReference = reference,
                });
            }
        }

        await db.SaveChangesAsync(ct);
    }

    // Per-deck art, not the global 3-slot preference: a deck's own card list already
    // names the exact illustration the player chose for it (in the deckbuilder), so the
    // only question here is whether they still own enough copies of THAT reference —
    // any shortfall falls back to the group's default/base art, never to the global
    // UserCardArtPreference (that table is only consulted for tokens below, since a
    // token is never itself a deck line to check ownership against). Lines[i]
    // corresponds exactly to deck[i] — callers that need to splice a rewritten
    // reference back into a richer, deck[i]-shaped structure of their own (e.g. a BGA
    // middleware rebuilding a nested per-type deck view) rely on this positional
    // correlation, since a shortfall can split one input line into two output lines
    // (some copies keep the chosen art, the rest fall back to the default).
    public async Task<ApplyToDeckResponse> ApplyToDeckAsync(
        Guid userId, IReadOnlyList<OwnershipCheckItem> deck, CancellationToken ct)
    {
        if (deck.Count == 0)
            return new ApplyToDeckResponse([], []);

        var references = deck.Select(i => i.Reference).Distinct().ToList();
        var catalogByRef = await db.CardArtCatalog
            .Where(c => references.Contains(c.Reference))
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Reference, ct);

        // Only entries that might actually need a fallback (tracked prints) require
        // knowing their group's default art or the player's owned quantity.
        var trackedEntries = catalogByRef.Values.Where(c => !AltArtRules.IsInfinite(c)).ToList();

        var familyIds = trackedEntries.Select(c => c.FamilyId).Distinct().ToList();
        var rowsByGroup = familyIds.Count == 0
            ? new Dictionary<(int, string, string), List<CardArtCatalogEntry>>()
            : (await db.CardArtCatalog.Where(c => familyIds.Contains(c.FamilyId)).AsNoTracking().ToListAsync(ct))
                .GroupBy(r => (r.FamilyId, r.Faction, r.Rarity))
                .ToDictionary(g => g.Key, g => g.ToList());

        var trackedRefs = trackedEntries.Select(c => c.Reference).Distinct().ToList();
        var owned = trackedRefs.Count == 0
            ? new Dictionary<string, int>()
            : await db.CardOwnerships
                .Where(o => o.UserId == userId && trackedRefs.Contains(o.CardReference))
                .AsNoTracking()
                .ToDictionaryAsync(o => o.CardReference, o => o.Quantity, ct);

        var lines = new List<IReadOnlyList<OwnershipCheckItem>>(deck.Count);
        foreach (var item in deck)
        {
            if (!catalogByRef.TryGetValue(item.Reference, out var entry) || AltArtRules.IsInfinite(entry))
            {
                lines.Add([item]); // not a catalog reference, or an untracked/always-available print
                continue;
            }

            var ownedQty = owned.GetValueOrDefault(item.Reference, 0);
            var defaultReference = ResolveDefaultRow(rowsByGroup[(entry.FamilyId, entry.Faction, entry.Rarity)]).Reference;

            // Owns enough of exactly this print, or this IS already the group's default
            // (no better fallback exists) -> nothing to rewrite.
            if (ownedQty >= item.Quantity || item.Reference == defaultReference)
            {
                lines.Add([item]);
                continue;
            }

            var line = new List<OwnershipCheckItem>();
            if (ownedQty > 0)
                line.Add(new OwnershipCheckItem(item.Reference, ownedQty));
            line.Add(new OwnershipCheckItem(defaultReference, item.Quantity - ownedQty));
            lines.Add(line);
        }

        // Tokens are never deck cards themselves (they're created by other cards'
        // effects, never listed as an owned/played copy) — the deckbuilder never asks
        // for one, so their art can only come from the global preference. Surfaced
        // separately (never mixed into Lines, which stays strictly positional against
        // the input) as one line item per token the player has explicitly chosen an art
        // for, one copy per token (max 1 slot).
        var tokens = await ResolveSelectedTokenItemsAsync(userId, ct);

        return new ApplyToDeckResponse(lines, tokens);
    }

    private async Task<List<OwnershipCheckItem>> ResolveSelectedTokenItemsAsync(Guid userId, CancellationToken ct)
    {
        var prefsByGroup = await LoadPreferencesByGroupAsync(userId, ct);
        if (prefsByGroup.Count == 0)
            return [];

        var familyIds = prefsByGroup.Keys.Select(k => k.Item1).Distinct().ToList();
        var cardTypeByGroup = (await db.CardArtCatalog
                .Where(c => familyIds.Contains(c.FamilyId))
                .AsNoTracking()
                .Select(c => new { c.FamilyId, c.Faction, c.Rarity, c.CardType })
                .ToListAsync(ct))
            .GroupBy(r => (r.FamilyId, r.Faction, r.Rarity))
            .ToDictionary(g => g.Key, g => g.First().CardType);

        var items = new List<OwnershipCheckItem>();
        foreach (var (groupKey, groupPrefs) in prefsByGroup)
        {
            if (cardTypeByGroup.GetValueOrDefault(groupKey) != "TOKEN")
                continue;

            // Tokens are capped at slot 1 (AltArtRules.MaxSlots), so an explicit choice
            // can only ever live there.
            if (groupPrefs.TryGetValue(1, out var chosenReference))
                items.Add(new OwnershipCheckItem(chosenReference, 1));
        }

        return items;
    }

    private async Task<Dictionary<(int, string, string), Dictionary<int, string>>> LoadPreferencesByGroupAsync(
        Guid userId, CancellationToken ct)
    {
        var prefs = await db.UserCardArtPreferences
            .Where(p => p.UserId == userId)
            .AsNoTracking()
            .ToListAsync(ct);

        return prefs
            .GroupBy(p => (p.FamilyId, p.Faction, p.Rarity))
            .ToDictionary(g => g.Key, g => g.ToDictionary(p => p.SlotIndex, p => p.PreferredReference));
    }

    private static List<AltArtSlotChoice> ResolveSlots(
        int maxSlots, string defaultReference, Dictionary<int, string>? explicitChoices)
    {
        var slots = new List<AltArtSlotChoice>(maxSlots);
        for (var slot = 1; slot <= maxSlots; slot++)
        {
            if (explicitChoices is not null && explicitChoices.TryGetValue(slot, out var chosen))
                slots.Add(new AltArtSlotChoice(slot, chosen, true));
            else
                slots.Add(new AltArtSlotChoice(slot, defaultReference, false));
        }
        return slots;
    }

    // The group's default art: the untracked "everyone has it" printing if the group has
    // one (there's normally at most one — see CardReferenceParser.IsAlternateArt) so new
    // players are never defaulted onto a scarce print they may own zero copies of. Only
    // when a group is made entirely of tracked/alternate prints does this fall back to
    // the earliest (lowest SortOrder) non-promo one, or the earliest overall if every
    // printing is a promo.
    private static CardArtCatalogEntry ResolveDefaultRow(IReadOnlyList<CardArtCatalogEntry> groupRows)
    {
        var untracked = groupRows.Where(r => !CardReferenceParser.IsAlternateArt(r.Reference)).ToList();
        if (untracked.Count > 0)
            return untracked.OrderBy(r => r.SortOrder).First();

        var nonPromo = groupRows.Where(r => !r.IsPromo).ToList();
        var pool = nonPromo.Count > 0 ? nonPromo : groupRows;
        return pool.OrderBy(r => r.SortOrder).First();
    }

    private static bool MatchesNumeric(int? value, NumericFilter f)
    {
        if (f.IsEmpty) return true;
        if (value is null) return false;
        var v = value.Value;
        if (f.Exact is { } e && v != e) return false;
        if (f.Gte is { } gte && v < gte) return false;
        if (f.Lte is { } lte && v > lte) return false;
        if (f.Gt is { } gt && v <= gt) return false;
        if (f.Lt is { } lt && v >= lt) return false;
        if (f.BetweenMin is { } bmin && v < bmin) return false;
        if (f.BetweenMax is { } bmax && v > bmax) return false;
        return true;
    }
}
