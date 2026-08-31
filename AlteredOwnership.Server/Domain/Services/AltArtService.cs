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

            var options = groupRows
                .OrderBy(r => r.SortOrder)
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

    public async Task<List<OwnershipCheckItem>> ApplyToDeckAsync(
        Guid userId, IReadOnlyList<OwnershipCheckItem> deck, CancellationToken ct)
    {
        if (deck.Count == 0)
            return [];

        var references = deck.Select(i => i.Reference).Distinct().ToList();
        var catalogByRef = await db.CardArtCatalog
            .Where(c => references.Contains(c.Reference))
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Reference, ct);

        var familyIds = catalogByRef.Values.Select(c => c.FamilyId).Distinct().ToList();
        var rowsByGroup = familyIds.Count == 0
            ? new Dictionary<(int, string, string), List<CardArtCatalogEntry>>()
            : (await db.CardArtCatalog.Where(c => familyIds.Contains(c.FamilyId)).AsNoTracking().ToListAsync(ct))
                .GroupBy(r => (r.FamilyId, r.Faction, r.Rarity))
                .ToDictionary(g => g.Key, g => g.ToList());

        var prefsByGroup = await LoadPreferencesByGroupAsync(userId, ct);

        var output = new List<OwnershipCheckItem>();
        var processedGroups = new HashSet<(int, string, string)>();
        foreach (var item in deck)
        {
            if (!catalogByRef.TryGetValue(item.Reference, out var entry))
            {
                output.Add(item);
                continue;
            }

            var groupKey = (entry.FamilyId, entry.Faction, entry.Rarity);
            var groupRows = rowsByGroup[groupKey];
            if (groupRows.Select(r => r.Reference).Distinct().Count() <= 1)
            {
                output.Add(item);
                continue;
            }

            processedGroups.Add(groupKey);
            var maxSlots = AltArtRules.MaxSlots(entry.CardType);
            var defaultReference = ResolveDefaultRow(groupRows).Reference;
            prefsByGroup.TryGetValue(groupKey, out var groupPrefs);
            var slots = ResolveSlots(maxSlots, defaultReference, groupPrefs);

            var chosenCounts = new Dictionary<string, int>();
            var slotsToUse = Math.Min(item.Quantity, maxSlots);
            for (var i = 0; i < slotsToUse; i++)
                chosenCounts[slots[i].Reference] = chosenCounts.GetValueOrDefault(slots[i].Reference) + 1;

            // A deck is never legally more than maxSlots copies of one card, but if it
            // somehow is, the extra copies fall on the default art rather than being lost.
            var overflow = item.Quantity - slotsToUse;
            if (overflow > 0)
                chosenCounts[defaultReference] = chosenCounts.GetValueOrDefault(defaultReference) + overflow;

            foreach (var (reference, quantity) in chosenCounts)
                output.Add(new OwnershipCheckItem(reference, quantity));
        }

        // Tokens are never deck cards themselves (they're created by other cards'
        // effects, never listed as an owned/played copy), so they can never arrive via
        // a deck's own item list — add each token the player has explicitly chosen an
        // art for as its own line item instead, one copy per token (max 1 slot).
        foreach (var tokenItem in await ResolveSelectedTokenItemsAsync(prefsByGroup, processedGroups, ct))
            output.Add(tokenItem);

        return output;
    }

    private async Task<List<OwnershipCheckItem>> ResolveSelectedTokenItemsAsync(
        Dictionary<(int, string, string), Dictionary<int, string>> prefsByGroup,
        HashSet<(int, string, string)> processedGroups,
        CancellationToken ct)
    {
        var candidateGroups = prefsByGroup.Keys.Where(k => !processedGroups.Contains(k)).ToList();
        if (candidateGroups.Count == 0)
            return [];

        var familyIds = candidateGroups.Select(k => k.Item1).Distinct().ToList();
        var cardTypeByGroup = (await db.CardArtCatalog
                .Where(c => familyIds.Contains(c.FamilyId))
                .AsNoTracking()
                .Select(c => new { c.FamilyId, c.Faction, c.Rarity, c.CardType })
                .ToListAsync(ct))
            .GroupBy(r => (r.FamilyId, r.Faction, r.Rarity))
            .ToDictionary(g => g.Key, g => g.First().CardType);

        var items = new List<OwnershipCheckItem>();
        foreach (var groupKey in candidateGroups)
        {
            if (cardTypeByGroup.GetValueOrDefault(groupKey) != "TOKEN")
                continue;

            // Tokens are capped at slot 1 (AltArtRules.MaxSlots), so an explicit choice
            // can only ever live there.
            if (prefsByGroup[groupKey].TryGetValue(1, out var chosenReference))
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

    // The group's default art: the earliest (lowest SortOrder) non-promo printing, or —
    // if every printing in the group is a promo — the earliest printing overall.
    private static CardArtCatalogEntry ResolveDefaultRow(IReadOnlyList<CardArtCatalogEntry> groupRows)
    {
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
