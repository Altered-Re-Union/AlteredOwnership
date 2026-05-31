using Microsoft.Extensions.Primitives;

namespace AlteredOwnership.Server.Endpoints;

// A numeric filter supporting exact match (?mainCost=6) or range operators
// (?mainCost[gte]=2&mainCost[lte]=5, ?mainCost[between]=2..5). Default = empty.
public readonly record struct NumericFilter(
    int? Exact, int? Gte, int? Lte, int? Gt, int? Lt, int? BetweenMin, int? BetweenMax)
{
    public bool IsEmpty =>
        Exact is null && Gte is null && Lte is null && Gt is null && Lt is null
        && BetweenMin is null && BetweenMax is null;
}

// Filter set for GET /api/collection, mirroring the existing card search interface:
// array params (?type[]=A&type[]=B → IN), numeric exact/range params and booleans. Bound
// from the raw query string so bracketed keys work verbatim. The `locale` selector is a
// separate presentation concern bound by the endpoint, not part of the filter.
public sealed class CollectionQuery
{
    // Case-insensitive substring match on the card name in the requested locale. Applied in
    // memory after localization (the jsonb name column is opaque to SQL via its converter).
    public string? Name { get; init; }
    public IReadOnlyList<string> Sets { get; init; } = [];
    public IReadOnlyList<string> Factions { get; init; } = [];
    public IReadOnlyList<string> Rarities { get; init; } = [];
    public IReadOnlyList<string> Types { get; init; } = [];
    public IReadOnlyList<string> Variations { get; init; } = [];
    public IReadOnlyList<string> SubTypes { get; init; } = [];
    public bool? IsBanned { get; init; }
    public bool? IsSuspended { get; init; }
    public NumericFilter MainCost { get; init; }
    public NumericFilter RecallCost { get; init; }
    public NumericFilter Forest { get; init; }
    public NumericFilter Mountain { get; init; }
    public NumericFilter Ocean { get; init; }

    public static ValueTask<CollectionQuery?> BindAsync(HttpContext ctx)
    {
        var q = ctx.Request.Query;

        var name = q["name"].FirstOrDefault();

        var query = new CollectionQuery
        {
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            Sets = Array(q, "set[]"),
            Factions = Array(q, "faction[]"),
            Rarities = Array(q, "rarity[]"),
            Types = Array(q, "type[]"),
            Variations = Array(q, "variation[]"),
            SubTypes = Array(q, "subtype[]"),
            IsBanned = Bool(q, "isBanned"),
            IsSuspended = Bool(q, "isSuspended"),
            MainCost = Numeric(q, "mainCost"),
            RecallCost = Numeric(q, "recallCost"),
            Forest = Numeric(q, "forest"),
            Mountain = Numeric(q, "mountain"),
            Ocean = Numeric(q, "ocean"),
        };
        return ValueTask.FromResult<CollectionQuery?>(query);
    }

    private static IReadOnlyList<string> Array(IQueryCollection q, string key) =>
        q[key].Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToList();

    private static bool? Bool(IQueryCollection q, string key) =>
        bool.TryParse(q[key].FirstOrDefault(), out var b) ? b : null;

    private static NumericFilter Numeric(IQueryCollection q, string field)
    {
        var between = q[$"{field}[between]"].FirstOrDefault();
        int? betweenMin = null, betweenMax = null;
        if (between is not null && between.Contains(".."))
        {
            var parts = between.Split("..", 2);
            if (int.TryParse(parts[0], out var min)) betweenMin = min;
            if (int.TryParse(parts[1], out var max)) betweenMax = max;
        }

        return new NumericFilter(
            Exact: Int(q, field),
            Gte: Int(q, $"{field}[gte]"),
            Lte: Int(q, $"{field}[lte]"),
            Gt: Int(q, $"{field}[gt]"),
            Lt: Int(q, $"{field}[lt]"),
            BetweenMin: betweenMin,
            BetweenMax: betweenMax);
    }

    private static int? Int(IQueryCollection q, string key) =>
        int.TryParse(q[key].FirstOrDefault(), out var v) ? v : null;
}
