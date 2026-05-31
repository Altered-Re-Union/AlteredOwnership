using System.Linq.Expressions;
using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using AlteredOwnership.Server.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace AlteredOwnership.Server.Domain.Services;

public class CollectionReader(OwnershipDbContext db)
{
    public async Task<IReadOnlyList<CardCollectionItemResponse>> GetCollectionAsync(
        Guid userId, CollectionQuery q, string locale, CancellationToken ct)
    {
        var owned = db.CardOwnerships.Where(c => c.UserId == userId);
        var filter = BuildCardFilter(q);

        // Single round-trip. No filter -> LEFT join the whole catalog so owned-but-
        // uncatalogued cards stay visible. With a filter -> INNER join the matching catalog
        // subset, which both applies the filter and drops uncatalogued cards.
        var joined = filter is null
            ? from co in owned
              join c in db.Cards on co.CardReference equals c.Reference into gj
              from card in gj.DefaultIfEmpty()
              select new { co, card = (Card?)card }
            : from co in owned
              join c in filter on co.CardReference equals c.Reference
              select new { co, card = (Card?)c };

        // Pull each owned row with its (nullable) catalog fields, then resolve the localized
        // name/image in memory — the result is one user's collection, so it's small.
        var rows = await joined
            .Select(x => new
            {
                x.co.CardReference,
                x.co.Quantity,
                Name = x.card != null ? x.card.Name : null,
                ImagePath = x.card != null ? x.card.ImagePath : null,
                Set = x.card != null ? x.card.Set : null,
                Faction = x.card != null ? x.card.Faction : null,
                Rarity = x.card != null ? x.card.Rarity : null,
                CardType = x.card != null ? x.card.CardType : null,
                Variation = x.card != null ? x.card.Variation : null,
                SubTypes = x.card != null ? x.card.SubTypes : null,
                IsBanned = x.card != null ? (bool?)x.card.IsBanned : null,
                IsSuspended = x.card != null ? (bool?)x.card.IsSuspended : null,
                MainCost = x.card != null ? x.card.MainCost : null,
                RecallCost = x.card != null ? x.card.RecallCost : null,
                Forest = x.card != null ? x.card.Forest : null,
                Mountain = x.card != null ? x.card.Mountain : null,
                Ocean = x.card != null ? x.card.Ocean : null,
            })
            .AsNoTracking()
            .ToListAsync(ct);

        var items = rows
            .Select(r => new CardCollectionItemResponse(
                r.CardReference, r.Quantity,
                Localize(r.Name, locale), Localize(r.ImagePath, locale),
                r.Set, r.Faction, r.Rarity, r.CardType, r.Variation, r.SubTypes,
                r.IsBanned, r.IsSuspended,
                r.MainCost, r.RecallCost, r.Forest, r.Mountain, r.Ocean));

        // Name search runs on the localized name (the jsonb column is opaque to SQL).
        if (!string.IsNullOrWhiteSpace(q.Name))
            items = items.Where(i =>
                i.Name is not null && i.Name.Contains(q.Name, StringComparison.OrdinalIgnoreCase));

        return items.ToList();
    }

    // Picks the requested locale, falling back to English.
    private static string? Localize(Dictionary<string, string>? text, string locale)
    {
        if (text is null || text.Count == 0)
            return null;
        return text.TryGetValue(locale, out var value) ? value : text.GetValueOrDefault("en");
    }

    // Returns the catalog filtered by the query's card clauses, or null when no card filter
    // is set. Every clause is on a plain Card column, so the whole thing translates to SQL.
    private IQueryable<Card>? BuildCardFilter(CollectionQuery q)
    {
        IQueryable<Card> cards = db.Cards;
        var filtered = cards;

        if (q.Sets.Count > 0) filtered = filtered.Where(c => q.Sets.Contains(c.Set));
        if (q.Factions.Count > 0) filtered = filtered.Where(c => q.Factions.Contains(c.Faction));
        if (q.Rarities.Count > 0) filtered = filtered.Where(c => q.Rarities.Contains(c.Rarity));
        if (q.Types.Count > 0) filtered = filtered.Where(c => q.Types.Contains(c.CardType));
        if (q.Variations.Count > 0) filtered = filtered.Where(c => q.Variations.Contains(c.Variation));
        if (q.SubTypes.Count > 0) filtered = filtered.Where(c => c.SubTypes.Any(s => q.SubTypes.Contains(s)));
        if (q.IsBanned is { } banned) filtered = filtered.Where(c => c.IsBanned == banned);
        if (q.IsSuspended is { } suspended) filtered = filtered.Where(c => c.IsSuspended == suspended);

        filtered = ApplyNumeric(filtered, q.MainCost, c => c.MainCost);
        filtered = ApplyNumeric(filtered, q.RecallCost, c => c.RecallCost);
        filtered = ApplyNumeric(filtered, q.Forest, c => c.Forest);
        filtered = ApplyNumeric(filtered, q.Mountain, c => c.Mountain);
        filtered = ApplyNumeric(filtered, q.Ocean, c => c.Ocean);

        return ReferenceEquals(filtered, cards) ? null : filtered;
    }

    // Builds a single predicate combining the filter's operators against `selector` and
    // applies it. Comparisons use liftToNull:false so the lambda is Func<Card, bool>; a card
    // with a null stat is excluded — the desired behaviour when filtering on that stat.
    private static IQueryable<Card> ApplyNumeric(
        IQueryable<Card> query, NumericFilter f, Expression<Func<Card, int?>> selector)
    {
        if (f.IsEmpty) return query;

        var param = selector.Parameters[0];
        var value = selector.Body;
        Expression? predicate = null;

        void Add(Expression cmp) => predicate = predicate is null ? cmp : Expression.AndAlso(predicate, cmp);
        static Expression Const(int v) => Expression.Constant((int?)v, typeof(int?));

        if (f.Exact is { } e) Add(Expression.Equal(value, Const(e), liftToNull: false, method: null));
        if (f.Gte is { } gte) Add(Expression.GreaterThanOrEqual(value, Const(gte), liftToNull: false, method: null));
        if (f.Lte is { } lte) Add(Expression.LessThanOrEqual(value, Const(lte), liftToNull: false, method: null));
        if (f.Gt is { } gt) Add(Expression.GreaterThan(value, Const(gt), liftToNull: false, method: null));
        if (f.Lt is { } lt) Add(Expression.LessThan(value, Const(lt), liftToNull: false, method: null));
        if (f.BetweenMin is { } bmin) Add(Expression.GreaterThanOrEqual(value, Const(bmin), liftToNull: false, method: null));
        if (f.BetweenMax is { } bmax) Add(Expression.LessThanOrEqual(value, Const(bmax), liftToNull: false, method: null));

        if (predicate is null) return query;
        return query.Where(Expression.Lambda<Func<Card, bool>>(predicate, param));
    }
}
