using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Endpoints;
using Microsoft.EntityFrameworkCore;

namespace AlteredOwnership.Server.Domain.Services;

public class OwnershipVerifier(OwnershipDbContext db)
{
    // Returns, for the requested cards, every shortfall where the user owns fewer copies
    // than asked for. Only unique / alt-art cards are verified — commons and other freely
    // available cards are dropped up front and never reported as missing.
    public async Task<IReadOnlyList<OwnershipShortfall>> VerifyAsync(
        Guid userId, IReadOnlyList<OwnershipCheckItem> items, CancellationToken ct)
    {
        var required = items
            .Where(i => CardReferenceParser.IsUnique(i.Reference) || CardReferenceParser.IsAlternateArt(i.Reference))
            .GroupBy(i => i.Reference)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Quantity));

        if (required.Count == 0)
            return [];

        var refs = required.Keys.ToList();
        var owned = await db.CardOwnerships
            .Where(c => c.UserId == userId && refs.Contains(c.CardReference))
            .AsNoTracking()
            .ToDictionaryAsync(c => c.CardReference, c => c.Quantity, ct);

        return required
            .Select(r => (Reference: r.Key, Requested: r.Value, Owned: owned.GetValueOrDefault(r.Key, 0)))
            .Where(x => x.Owned < x.Requested)
            .Select(x => new OwnershipShortfall(x.Reference, x.Requested, x.Owned))
            .ToList();
    }
}
