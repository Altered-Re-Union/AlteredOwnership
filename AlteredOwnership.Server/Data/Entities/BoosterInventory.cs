namespace AlteredOwnership.Server.Data.Entities;

// Per-user unopened booster counts, re-derived from OwnershipEvents just like
// CardOwnership. A row is deleted rather than kept at 0 (see ProjectionReconciler).
public class BoosterInventory
{
    public Guid UserId { get; set; }

    public string BoosterTypeKey { get; set; } = default!;

    public int Quantity { get; set; }
}
