namespace AlteredOwnership.Server.Data.Entities;

// Every known non-unique printing of every card, regardless of who owns it — sourced
// from CardsData's CardFamilies.csv + per-set NonUniquePrints.csv. Not event-sourced:
// a reference catalog, not a per-user projection (mirrors UniqueCardStock). Unique
// cards (including heroes' one-of-a-kind serialized prints) have no alternate art and
// are never seeded here.
public class CardArtCatalogEntry
{
    public string Reference { get; set; } = default!;

    public int FamilyId { get; set; }

    public Dictionary<string, string> FamilyName { get; set; } = new();

    public string CardType { get; set; } = default!;

    public string Faction { get; set; } = default!;

    public string Rarity { get; set; } = default!;

    public string Set { get; set; } = default!;

    public bool IsPromo { get; set; }

    // From CardsData's Sets.csv — a token printed in a base set is available to every
    // player regardless of ownership (see AltArtRules.IsInfinite); non-token prints
    // don't currently use this flag.
    public bool IsBaseSet { get; set; }

    public int? MainCost { get; set; }

    // CardsData's own print id, preserved to know which printing came out first across
    // sets — a family/faction/rarity group's default art is its lowest SortOrder.
    public int SortOrder { get; set; }
}
