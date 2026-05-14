using AlteredOwnership.Server.Data;
using AlteredOwnership.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AlteredOwnership.Server.Domain.Services;

public class CollectionReader(OwnershipDbContext db)
{
    public async Task<IReadOnlyList<CardOwnership>> GetCollectionAsync(Guid userId, CancellationToken ct)
    {
        return await db.CardOwnerships
            .Where(c => c.UserId == userId)
            .AsNoTracking()
            .ToListAsync(ct);
    }
}
