namespace AlteredOwnership.Server.Data.Entities;

// A player's explicit choice of illustration for one copy ("exemplaire") within an
// alt-art group (FamilyId, Faction, Rarity). SlotIndex is 1-based, up to 3 (1 for
// HERO-type cards, which are capped at one copy per deck). No row for a slot means
// "use the group's default art" (the earliest, non-promo printing) — current state,
// not event-sourced: a display preference, not a scarce resource to replay.
public class UserCardArtPreference
{
    public Guid UserId { get; set; }

    public int FamilyId { get; set; }

    public string Faction { get; set; } = default!;

    public string Rarity { get; set; } = default!;

    public int SlotIndex { get; set; }

    public string PreferredReference { get; set; } = default!;
}
