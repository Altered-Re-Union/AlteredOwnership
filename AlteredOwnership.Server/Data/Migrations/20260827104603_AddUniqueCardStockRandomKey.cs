using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlteredOwnership.Server.Data.Migrations
{
    /// <inheritdoc />
    // Hand-edited twice now:
    //
    // 1st: the original `ADD COLUMN ... DEFAULT (random())` forces Postgres to rewrite
    // the whole table to compute a value for every existing row, and at ~5.4M rows that
    // took longer than the migration runner's 30s command timeout ("canceling statement
    // due to user request" after ~30s on exactly that ALTER TABLE, confirmed in prod
    // logs) — restructured into: add the column nullable (instant), backfill in bounded
    // batches (many small UPDATEs, each its own command/timeout budget), only then
    // enforce NOT NULL + SET DEFAULT, then build the three new indexes CONCURRENTLY so
    // live booster-opens (which UPDATE this same table) never stall behind an index
    // build's lock.
    //
    // 2nd: everything up to (not including) the first CONCURRENTLY statement runs in one
    // transaction and committed successfully in prod — then one of the three
    // CREATE INDEX CONCURRENTLY calls failed. CONCURRENTLY can't run in a transaction, so
    // EF never got to mark this migration as applied, and the whole Up() reran from the
    // top on every retry — including DropIndex, which now failed every time ("index ...
    // does not exist") since the already-committed part had already dropped it, crash-
    // looping the migrations container indefinitely. Every statement below is now
    // idempotent (IF EXISTS / IF NOT EXISTS, or naturally a no-op on re-run) specifically
    // so a retry from ANY partial state — including one left by a previously interrupted
    // CONCURRENTLY build, which Postgres leaves behind as a real but INVALID index of
    // that same name, not merely absent — always converges instead of getting stuck
    // re-failing on the same non-idempotent step.
    public partial class AddUniqueCardStockRandomKey : Migration
    {
        // ~5,455,928 rows in prod today; 250k/batch × 100 batches = 25M capacity — a
        // deliberately generous multiple of the current size (uniques are a one-time
        // print run per set, so this table is effectively static, not growing toward
        // that ceiling) rather than a tightly-sized count. A trailing batch that finds
        // nothing left to fill just runs its SELECT and updates zero rows — a few tens
        // of milliseconds — so overshooting real demand this much costs almost nothing,
        // while undershooting it fails the SET NOT NULL below outright (found the hard
        // way: an earlier version sized for exactly the current row count came up short
        // in local testing the moment the table had more rows than that count expected).
        private const int BackfillBatchSize = 250_000;
        private const int BackfillBatchCount = 100;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_UniqueCardStock_Set_IsDistributed";""");

            migrationBuilder.Sql("""ALTER TABLE "UniqueCardStock" ADD COLUMN IF NOT EXISTS "RandomKey" double precision;""");

            foreach (var sql in BuildBackfillBatches())
                migrationBuilder.Sql(sql);

            migrationBuilder.Sql("""ALTER TABLE "UniqueCardStock" ALTER COLUMN "RandomKey" SET NOT NULL;""");
            migrationBuilder.Sql("""ALTER TABLE "UniqueCardStock" ALTER COLUMN "RandomKey" SET DEFAULT random();""");

            // Unconditional drop-then-create rather than "CREATE ... CONCURRENTLY IF NOT
            // EXISTS": IF NOT EXISTS only checks the name, so it would leave a prior
            // interrupted build's INVALID index (still present under that name, per
            // Postgres) exactly as broken as it found it. Dropping first — a no-op via
            // IF EXISTS when there's nothing there yet — makes every one of these three
            // always converge to a fresh, valid index regardless of what state a retry
            // finds them in. Both DROP and CREATE CONCURRENTLY must run outside a
            // transaction block, hence suppressTransaction: true throughout this group.
            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_UniqueCardStock_Faction_IsDistributed_RandomKey";""",
                suppressTransaction: true);
            migrationBuilder.Sql(
                """CREATE INDEX CONCURRENTLY "IX_UniqueCardStock_Faction_IsDistributed_RandomKey" ON "UniqueCardStock" ("Faction", "IsDistributed", "RandomKey");""",
                suppressTransaction: true);

            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_UniqueCardStock_IsDistributed_RandomKey";""",
                suppressTransaction: true);
            migrationBuilder.Sql(
                """CREATE INDEX CONCURRENTLY "IX_UniqueCardStock_IsDistributed_RandomKey" ON "UniqueCardStock" ("IsDistributed", "RandomKey");""",
                suppressTransaction: true);

            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_UniqueCardStock_Set_IsDistributed_RandomKey";""",
                suppressTransaction: true);
            migrationBuilder.Sql(
                """CREATE INDEX CONCURRENTLY "IX_UniqueCardStock_Set_IsDistributed_RandomKey" ON "UniqueCardStock" ("Set", "IsDistributed", "RandomKey");""",
                suppressTransaction: true);
        }

        private static IEnumerable<string> BuildBackfillBatches()
        {
            for (var i = 0; i < BackfillBatchCount; i++)
            {
                yield return $"""
                    UPDATE "UniqueCardStock"
                    SET "RandomKey" = random()
                    WHERE ctid IN (
                        SELECT ctid FROM "UniqueCardStock"
                        WHERE "RandomKey" IS NULL
                        LIMIT {BackfillBatchSize});
                    """;
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_UniqueCardStock_Faction_IsDistributed_RandomKey";""",
                suppressTransaction: true);
            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_UniqueCardStock_IsDistributed_RandomKey";""",
                suppressTransaction: true);
            migrationBuilder.Sql(
                """DROP INDEX CONCURRENTLY IF EXISTS "IX_UniqueCardStock_Set_IsDistributed_RandomKey";""",
                suppressTransaction: true);

            migrationBuilder.Sql("""ALTER TABLE "UniqueCardStock" DROP COLUMN IF EXISTS "RandomKey";""");

            migrationBuilder.Sql(
                """CREATE INDEX IF NOT EXISTS "IX_UniqueCardStock_Set_IsDistributed" ON "UniqueCardStock" ("Set", "IsDistributed");""");
        }
    }
}
