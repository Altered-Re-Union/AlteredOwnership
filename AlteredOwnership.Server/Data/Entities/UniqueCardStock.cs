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
}
