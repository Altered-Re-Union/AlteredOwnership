using AlteredOwnership.Server.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AlteredOwnership.Server.Domain.Services;

public class NoUniqueStockAvailableException(string? set)
    : Exception(set is null
        ? "No undistributed unique card left in stock."
        : $"No undistributed unique card left in stock for set '{set}'.")
{
    public string? Set { get; } = set;
}

public class UniqueStockService(OwnershipDbContext db)
{
    // Atomically claims one undistributed unique (optionally restricted to a set) and
    // flags it distributed. FOR UPDATE SKIP LOCKED means concurrent callers never pick
    // the same row and never block each other.
    public async Task<string> ReserveRandomAsync(string? set, CancellationToken ct)
    {
        // The explicit ::text cast is required: Postgres can't infer a type for a
        // parameter used only in "param IS NULL" (error 42P18) without one.
        var refs = await db.Database.SqlQuery<string>($"""
            UPDATE "UniqueCardStock" SET "IsDistributed" = true
            WHERE "CardReference" = (
                SELECT "CardReference" FROM "UniqueCardStock"
                WHERE "IsDistributed" = false AND ({set}::text IS NULL OR "Set" = {set}::text)
                ORDER BY random() LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING "CardReference"
            """).ToListAsync(ct);

        return refs.SingleOrDefault() ?? throw new NoUniqueStockAvailableException(set);
    }
}
