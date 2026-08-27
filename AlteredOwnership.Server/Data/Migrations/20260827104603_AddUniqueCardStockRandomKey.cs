using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlteredOwnership.Server.Data.Migrations
{
    /// <inheritdoc />
    // Hand-edited after the first prod attempt timed out: `ADD COLUMN ... DEFAULT
    // (random())` forces Postgres to rewrite the whole table to compute a value for
    // every existing row, and at ~5.4M rows that took longer than the migration
    // runner's 30s command timeout (confirmed in prod logs — "canceling statement due
    // to user request" after ~30s on exactly that ALTER TABLE). Restructured into
    // steps no single command should come close to that budget for:
    //   1. add the column nullable, no default (metadata-only, instant)
    //   2. backfill in bounded batches — many small UPDATEs (each its own command, so
    //      each gets its own fresh 30s budget) instead of one all-at-once statement
    //   3. only now enforce NOT NULL (a read-only validation scan, not a rewrite) and
    //      set the default for future inserts (metadata-only)
    //   4. build the three new indexes CONCURRENTLY — avoids the exclusive-ish lock a
    //      plain CREATE INDEX would hold for the whole build, which would otherwise
    //      stall every live booster-open (an UPDATE on this same table) for as long as
    //      each index takes to build. CONCURRENTLY cannot run inside a transaction
    //      block, hence suppressTransaction: true on those three calls only.
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
            migrationBuilder.DropIndex(
                name: "IX_UniqueCardStock_Set_IsDistributed",
                table: "UniqueCardStock");

            migrationBuilder.Sql("""ALTER TABLE "UniqueCardStock" ADD "RandomKey" double precision;""");

            foreach (var sql in BuildBackfillBatches())
                migrationBuilder.Sql(sql);

            migrationBuilder.Sql("""ALTER TABLE "UniqueCardStock" ALTER COLUMN "RandomKey" SET NOT NULL;""");
            migrationBuilder.Sql("""ALTER TABLE "UniqueCardStock" ALTER COLUMN "RandomKey" SET DEFAULT random();""");

            migrationBuilder.Sql(
                """CREATE INDEX CONCURRENTLY "IX_UniqueCardStock_Faction_IsDistributed_RandomKey" ON "UniqueCardStock" ("Faction", "IsDistributed", "RandomKey");""",
                suppressTransaction: true);
            migrationBuilder.Sql(
                """CREATE INDEX CONCURRENTLY "IX_UniqueCardStock_IsDistributed_RandomKey" ON "UniqueCardStock" ("IsDistributed", "RandomKey");""",
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

            migrationBuilder.DropColumn(
                name: "RandomKey",
                table: "UniqueCardStock");

            migrationBuilder.CreateIndex(
                name: "IX_UniqueCardStock_Set_IsDistributed",
                table: "UniqueCardStock",
                columns: new[] { "Set", "IsDistributed" });
        }
    }
}
