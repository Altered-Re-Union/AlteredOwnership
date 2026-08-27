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
    //
    // Picks via each row's persisted RandomKey (see UniqueCardStock) rather than
    // `ORDER BY random()`: at this table's real size (~5.4M rows, up to ~200k
    // undistributed in a single Set/Faction pool) sorting every candidate by a
    // freshly computed random value took 1-2s per draw (measured in prod); an
    // indexed ">= anchor" range scan (falling back to "smallest key" when nothing
    // is >= anchor — i.e. the anchor landed past the end, or every forward candidate
    // was locked by a concurrent draw) is a few-row index lookup instead.
    public async Task<string> ReserveRandomAsync(string? set, string? faction, CancellationToken ct)
    {
        var anchor = Random.Shared.NextDouble();

        // Two separate queries (rather than one with a runtime OR toggle) so the
        // planner always sees a plain, static ">=" predicate on RandomKey — never
        // something like "@wrapAround OR RandomKey >= @anchor" it would have to prove
        // is trivially true to keep using the index for the ORDER BY.
        var reference =
            await TryReserveFromAnchorAsync(set, faction, anchor, ct) ??
            await TryReserveFromStartAsync(set, faction, ct);

        return reference ?? throw new NoUniqueStockAvailableException(set, faction);
    }

    // Forward scan: first undistributed match at/after the random anchor.
    private async Task<string?> TryReserveFromAnchorAsync(string? set, string? faction, double anchor, CancellationToken ct)
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
                    AND "RandomKey" >= {anchor}
                ORDER BY "RandomKey" ASC LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING "CardReference"
            """).ToListAsync(ct);

        return refs.SingleOrDefault();
    }

    // Wraparound: nothing matched at/after the anchor (it landed past every remaining
    // candidate's key, or every one of them was locked by a concurrent draw) — every
    // undistributed match left is necessarily before the anchor, so just take the
    // smallest key overall.
    private async Task<string?> TryReserveFromStartAsync(string? set, string? faction, CancellationToken ct)
    {
        var refs = await db.Database.SqlQuery<string>($"""
            UPDATE "UniqueCardStock" SET "IsDistributed" = true
            WHERE "CardReference" = (
                SELECT "CardReference" FROM "UniqueCardStock"
                WHERE "IsDistributed" = false
                    AND ({set}::text IS NULL OR "Set" = {set}::text)
                    AND ({faction}::text IS NULL OR "Faction" = {faction}::text)
                ORDER BY "RandomKey" ASC LIMIT 1
                FOR UPDATE SKIP LOCKED)
            RETURNING "CardReference"
            """).ToListAsync(ct);

        return refs.SingleOrDefault();
    }
}
