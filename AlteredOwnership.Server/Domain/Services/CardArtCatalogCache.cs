using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AlteredOwnership.Server.Domain.Services;

// Immutable in-memory copy of CardArtCatalog (a few thousand rows) with the facts
// apply-to-deck derives from it precomputed, so a request no longer reads the catalog
// at all. The catalog is reference data written only by migrations (ReseedCardArtCatalog*),
// which run before a new process serves traffic, so a snapshot taken by this process
// stays valid for its whole lifetime.
public sealed class CardArtCatalogSnapshot
{
    public IReadOnlyDictionary<string, CardArtCatalogEntry> ByReference { get; }
    public IReadOnlyDictionary<(int FamilyId, string Faction, string Rarity), List<CardArtCatalogEntry>> RowsByGroup { get; }

    private readonly Dictionary<(int, string, string), CardArtCatalogEntry> _defaultByGroup;
    private readonly HashSet<string> _infinite;

    public CardArtCatalogSnapshot(IReadOnlyList<CardArtCatalogEntry> rows)
    {
        ByReference = rows.ToDictionary(r => r.Reference);
        RowsByGroup = rows
            .GroupBy(r => (r.FamilyId, r.Faction, r.Rarity))
            .ToDictionary(g => g.Key, g => g.ToList());
        _defaultByGroup = RowsByGroup.ToDictionary(kv => kv.Key, kv => AltArtService.ResolveDefaultRow(kv.Value));
        _infinite = rows.Where(AltArtRules.IsInfinite).Select(r => r.Reference).ToHashSet();
    }

    public int Count => ByReference.Count;

    public bool IsInfinite(CardArtCatalogEntry entry) => _infinite.Contains(entry.Reference);

    public CardArtCatalogEntry DefaultRowOf((int FamilyId, string Faction, string Rarity) group) => _defaultByGroup[group];
}

// Loads the snapshot once, on first use (or from the startup warm-up, whichever comes
// first — concurrent callers share the one load), and keeps it for the process lifetime.
// A failed load is not remembered: the next caller retries.
public sealed class CardArtCatalogCache(IServiceScopeFactory scopes)
{
    private readonly object _gate = new();
    private Task<CardArtCatalogSnapshot>? _load;

    public Task<CardArtCatalogSnapshot> GetAsync(CancellationToken ct)
    {
        Task<CardArtCatalogSnapshot> load;
        lock (_gate)
        {
            load = _load ??= LoadAsync();
        }

        return load.WaitAsync(ct);
    }

    private async Task<CardArtCatalogSnapshot> LoadAsync()
    {
        try
        {
            // Deliberately not tied to the first caller's request: if that caller gives up,
            // the load still completes for everyone else.
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OwnershipDbContext>();
            var rows = await db.CardArtCatalog.AsNoTracking().ToListAsync();
            return new CardArtCatalogSnapshot(rows);
        }
        catch
        {
            lock (_gate)
            {
                _load = null;
            }

            throw;
        }
    }
}
