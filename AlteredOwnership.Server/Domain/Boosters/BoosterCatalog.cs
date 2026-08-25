namespace AlteredOwnership.Server.Domain.Boosters;

// A booster type's random-draw scope, name and cover art. Fixed in code
// for now — no admin UI to author these, per current scope. Every type today draws
// exactly one unique from UniqueCardStock, filtered by Set and/or Faction (either
// or both may be null to mean "any"); ImagePath is nullable when no cover art
// exists yet, and the frontend falls back to a generic icon.
public record BoosterType(string Key, string Name, string? Set, string? Faction, string? ImagePath);

public static class BoosterCatalog
{
    // Set codes match UniqueCardStock.Set / Card.Set (CardsData's Sets.csv
    // Reference column) — every set known to contain uniques as of 2026-08-24.
    //
    // Cover art: /img/boosters/unique-booster-template.webp (fan-made "Unique"
    // pack art) composited with, per pool: the official card-back swirl for the
    // fully-random pool, set key-art + logo for a set-scoped pool (FUGUE has no
    // official set logo yet, so it's key-art only), or a faction banner crop for
    // a faction-scoped pool.
    public static readonly IReadOnlyList<BoosterType> All =
    [
        new("UNIQUE_RANDOM", "Unique aléatoire", null, null, "/img/boosters/unique-random.webp"),
        new("UNIQUE_RANDOM_ALIZE", "Unique aléatoire Trial by Frost", "ALIZE", null, "/img/boosters/unique-random-alize.webp"),
        new("UNIQUE_RANDOM_BISE", "Unique aléatoire Whispers from the Maze", "BISE", null, "/img/boosters/unique-random-bise.webp"),
        new("UNIQUE_RANDOM_CORE", "Unique aléatoire Beyond the Gates", "CORE", null, "/img/boosters/unique-random-core.webp"),
        new("UNIQUE_RANDOM_COREKS", "Unique aléatoire Beyond the Gates - KS Edition", "COREKS", null, "/img/boosters/unique-random-coreks.webp"),
        new("UNIQUE_RANDOM_CYCLONE", "Unique aléatoire Skybound Odyssey", "CYCLONE", null, "/img/boosters/unique-random-cyclone.webp"),
        new("UNIQUE_RANDOM_DUSTER", "Unique aléatoire Seeds of Unity", "DUSTER", null, "/img/boosters/unique-random-duster.webp"),
        new("UNIQUE_RANDOM_EOLE", "Unique aléatoire Roots of Corruption", "EOLE", null, "/img/boosters/unique-random-eole.webp"),
        new("UNIQUE_RANDOM_FUGUE", "Unique aléatoire Neverending Journey", "FUGUE", null, "/img/boosters/unique-random-fugue.webp"),
        new("UNIQUE_RANDOM_AXIOM", "Unique aléatoire Axiom", null, "AX", "/img/boosters/unique-random-axiom.webp"),
        new("UNIQUE_RANDOM_BRAVOS", "Unique aléatoire Bravos", null, "BR", "/img/boosters/unique-random-bravos.webp"),
        new("UNIQUE_RANDOM_LYRA", "Unique aléatoire Lyra", null, "LY", "/img/boosters/unique-random-lyra.webp"),
        new("UNIQUE_RANDOM_MUNA", "Unique aléatoire Muna", null, "MU", "/img/boosters/unique-random-muna.webp"),
        new("UNIQUE_RANDOM_ORDIS", "Unique aléatoire Ordis", null, "OR", "/img/boosters/unique-random-ordis.webp"),
        new("UNIQUE_RANDOM_YZMIR", "Unique aléatoire Yzmir", null, "YZ", "/img/boosters/unique-random-yzmir.webp"),
    ];

    public static BoosterType? Find(string key) => All.FirstOrDefault(b => b.Key == key);
}
