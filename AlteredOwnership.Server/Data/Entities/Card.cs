namespace AlteredOwnership.Server.Data.Entities;

// Shared card catalog, one row per reference (no per-user duplication). Reference data
// fetched from cards.alteredcore.org during import — NOT an event-sourced projection.
// Localized text (Name, ImagePath) is stored per-language and resolved at read time.
public class Card
{
    public string Reference { get; set; } = default!;

    public Dictionary<string, string> Name { get; set; } = new();

    public Dictionary<string, string> ImagePath { get; set; } = new();

    public string Set { get; set; } = default!;

    public string Faction { get; set; } = default!;

    public string Rarity { get; set; } = default!;

    public string CardType { get; set; } = default!;

    public string Variation { get; set; } = default!;

    public List<string> SubTypes { get; set; } = new();

    public bool IsBanned { get; set; }

    public bool IsSuspended { get; set; }

    public int? MainCost { get; set; }

    public int? RecallCost { get; set; }

    public int? Forest { get; set; }

    public int? Mountain { get; set; }

    public int? Ocean { get; set; }
}
