namespace AlteredOwnership.Server.Domain.Boosters;

// A booster type's random-draw scope, name and (future) cover art. Fixed in code
// for now — no admin UI to author these, per current scope. Every type today draws
// exactly one unique from UniqueCardStock, filtered by Set and/or Faction (either
// or both may be null to mean "any"); ImagePath is nullable until real artwork
// exists, and the frontend falls back to a generic icon.
public record BoosterType(string Key, string Name, string? Set, string? Faction, string? ImagePath);

public static class BoosterCatalog
{
    // Set codes match UniqueCardStock.Set / Card.Set (CardsData's Sets.csv
    // Reference column) — every set known to contain uniques as of 2026-08-24.
    public static readonly IReadOnlyList<BoosterType> All =
    [
        new("UNIQUE_RANDOM", "Unique aléatoire", null, null, null),
        new("UNIQUE_RANDOM_ALIZE", "Unique aléatoire Trial by Frost", "ALIZE", null, null),
        new("UNIQUE_RANDOM_BISE", "Unique aléatoire Whispers from the Maze", "BISE", null, null),
        new("UNIQUE_RANDOM_CORE", "Unique aléatoire Beyond the Gates", "CORE", null, null),
        new("UNIQUE_RANDOM_COREKS", "Unique aléatoire Beyond the Gates - KS Edition", "COREKS", null, null),
        new("UNIQUE_RANDOM_CYCLONE", "Unique aléatoire Skybound Odyssey", "CYCLONE", null, null),
        new("UNIQUE_RANDOM_DUSTER", "Unique aléatoire Seeds of Unity", "DUSTER", null, null),
        new("UNIQUE_RANDOM_EOLE", "Unique aléatoire Roots of Corruption", "EOLE", null, null),
        new("UNIQUE_RANDOM_FUGUE", "Unique aléatoire Neverending Journey", "FUGUE", null, null),
        new("UNIQUE_RANDOM_AXIOM", "Unique aléatoire Axiom", null, "AX", null),
        new("UNIQUE_RANDOM_BRAVOS", "Unique aléatoire Bravos", null, "BR", null),
        new("UNIQUE_RANDOM_LYRA", "Unique aléatoire Lyra", null, "LY", null),
        new("UNIQUE_RANDOM_MUNA", "Unique aléatoire Muna", null, "MU", null),
        new("UNIQUE_RANDOM_ORDIS", "Unique aléatoire Ordis", null, "OR", null),
        new("UNIQUE_RANDOM_YZMIR", "Unique aléatoire Yzmir", null, "YZ", null),
    ];

    public static BoosterType? Find(string key) => All.FirstOrDefault(b => b.Key == key);
}
