using AlteredOwnership.Server.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AlteredOwnership.Server.Domain.Services;

public class NoUniqueStockAvailableException(string? set, string? faction = null)
    : Exception(set is null && faction is null
        ? "No undistributed unique card left in stock."
        : $"No undistributed unique card left in stock for {DescribeScope(set, faction)}.")
{
    public string? Set { get; } = set;
    public string? Faction { get; } = faction;

    private static string DescribeScope(string? set, string? faction) => (set, faction) switch
    {
        (not null, not null) => $"set '{set}', faction '{faction}'",
        (not null, null) => $"set '{set}'",
        (null, not null) => $"faction '{faction}'",
        _ => "any scope",
    };
}

public class UniqueStockService(OwnershipDbContext db)
{
    // Atomically claims one undistributed unique (optionally restricted to a set
    // and/or faction) and flags it distributed. FOR UPDATE SKIP LOCKED means
    // concurrent callers never pick the same row and never block each other.
    public async Task<string> ReserveRandomAsync(string? set, string? faction, CancellationToken ct)
    {
        // The explicit ::text cast is required: Postgres can't infer a type for a
        // parameter used only in "param IS NULL" (error 42P18) without one.
        var refs = await db.Database.SqlQuery<string>($"""
            UPDATE "UniqueCardStock" SET "IsDistributed" = true
            WHERE "CardReference" = (
                SELECT "CardReference" FROM "UniqueCardStock"
                WHERE "IsDistributed" = false
                    AND ({set}::text IS NULL OR "Set" = {set}::text)
                    AND ({faction}::text IS NULL OR "Faction" = {faction}::text)
                ORDER BY random() LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING "CardReference"
            """).ToListAsync(ct);

        return refs.SingleOrDefault() ?? throw new NoUniqueStockAvailableException(set, faction);
    }
}
