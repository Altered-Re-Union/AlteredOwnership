namespace AlteredOwnership.Server.Data.Entities;

// Ground truth for which unique cards exist and whether they've already been given
// out, seeded/maintained from CardsData's per-set UniquePrints.csv exports. Not
// event-sourced: this is a stock ledger, not a per-user projection.
public class UniqueCardStock
{
    public string CardReference { get; set; } = default!;

    public string Set { get; set; } = default!;

    public string Faction { get; set; } = default!;

    public bool IsDistributed { get; set; }

    // A random anchor assigned once per row (DB default `random()`, so every row —
    // including the ~5.4M seeded ones — gets its own independent, immutable value).
    // UniqueStockService.ReserveRandomAsync uses it to pick a uniformly random
    // undistributed row via an indexed range scan instead of `ORDER BY random()`,
    // which forces Postgres to compute+sort a random value for every candidate row —
    // fine on a small table, ~1-2s per draw at this table's real size (measured in
    // prod). See UniqueStockService for the query shape this column supports.
    public double RandomKey { get; set; }
}
