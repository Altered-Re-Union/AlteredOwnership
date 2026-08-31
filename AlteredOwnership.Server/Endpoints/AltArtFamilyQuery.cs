namespace AlteredOwnership.Server.Endpoints;

// Filter set for GET /api/alt-arts/families — deliberately no `set` filter (the whole
// point of this list is families that span multiple sets). Mirrors CollectionQuery's
// binding style; reuses its NumericFilter.
public sealed class AltArtFamilyQuery
{
    public string? Name { get; init; }
    public IReadOnlyList<string> Factions { get; init; } = [];
    public IReadOnlyList<string> Rarities { get; init; } = [];
    public NumericFilter MainCost { get; init; }

    public static ValueTask<AltArtFamilyQuery?> BindAsync(HttpContext ctx)
    {
        var q = ctx.Request.Query;
        var name = q["name"].FirstOrDefault();

        var query = new AltArtFamilyQuery
        {
            Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            Factions = Array(q, "faction[]"),
            Rarities = Array(q, "rarity[]"),
            MainCost = Numeric(q, "mainCost"),
        };
        return ValueTask.FromResult<AltArtFamilyQuery?>(query);
    }

    private static IReadOnlyList<string> Array(IQueryCollection q, string key) =>
        q[key].Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToList();

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
