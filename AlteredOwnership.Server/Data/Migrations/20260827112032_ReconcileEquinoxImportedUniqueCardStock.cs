using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlteredOwnership.Server.Data.Migrations
{
    /// <inheritdoc />
    // Data-only migration: checks whether UniqueCardStock.IsDistributed can be trusted.
    // ReserveRandomAsync (booster opening) is the only code path that ever sets it to
    // true — an Equinox import or an admin RewardEvent both add straight to the
    // CardOwnership projection (see EquinoxImportEvent/RewardEvent.ApplyV1) without
    // touching UniqueCardStock at all. So any unique a player already owned before this
    // ownership/booster system existed — the common case for an Equinox import — is
    // still flagged "available" here unless something reconciles it, which means a
    // booster could draw a unique someone already owns and fail the partial unique
    // index on CardOwnership ("IsUnique" WHERE true) at reconcile time.
    //
    // This fixes exactly the Equinox-import case (scope the user asked for — reward
    // grants are separate, much lower volume, and not touched here) and reports how big
    // the drift actually was, via RAISE WARNING (visible in the migration-apply logs)
    // and a small permanent table so the numbers can be checked later without having to
    // go dig through log history.
    public partial class ReconcileEquinoxImportedUniqueCardStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "UniqueCardStockReconciliationReport" (
                    "Id" bigserial PRIMARY KEY,
                    "RanAt" timestamptz NOT NULL DEFAULT now(),
                    "Source" text NOT NULL,
                    "TotalReferencesChecked" integer NOT NULL,
                    "StaleReferencesFixed" integer NOT NULL,
                    "OrphanReferences" integer NOT NULL
                );
                """);

            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    total_refs   integer;
                    stale_count  integer;
                    orphan_count integer;
                BEGIN
                    -- Every distinct unique CardReference that appears with a positive
                    -- quantity in any Equinox import payload. Joined against
                    -- CardOwnership.IsUnique (the app's own IsUnique classification,
                    -- reused here instead of re-deriving the reference-shape regex) rather
                    -- than parsing the reference string ourselves.
                    CREATE TEMP TABLE _imported_unique_refs ON COMMIT DROP AS
                    SELECT DISTINCT elem->>'Reference' AS "CardReference"
                    FROM "OwnershipEvents" e,
                         jsonb_array_elements(e."Payload"->'Cards') AS elem
                    WHERE e."Kind" = 'EquinoxImport'
                      AND (elem->>'Quantity')::int > 0
                      AND EXISTS (
                          SELECT 1 FROM "CardOwnerships" co
                          WHERE co."CardReference" = elem->>'Reference' AND co."IsUnique" = true
                      );

                    SELECT count(*) INTO total_refs FROM _imported_unique_refs;

                    -- Imported (and currently owned) uniques with no UniqueCardStock row at
                    -- all — can't happen from a genuine card print (the seed is meant to be
                    -- exhaustive), but not fixable by an UPDATE either way, so counted and
                    -- reported separately rather than silently ignored.
                    SELECT count(*) INTO orphan_count
                    FROM _imported_unique_refs r
                    WHERE NOT EXISTS (
                        SELECT 1 FROM "UniqueCardStock" s WHERE s."CardReference" = r."CardReference"
                    );

                    -- The actual drift: rows a booster could still legally draw even though
                    -- an import already gave the same card to a (possibly different) player.
                    SELECT count(*) INTO stale_count
                    FROM _imported_unique_refs r
                    JOIN "UniqueCardStock" s ON s."CardReference" = r."CardReference"
                    WHERE s."IsDistributed" = false;

                    UPDATE "UniqueCardStock" s
                    SET "IsDistributed" = true
                    FROM _imported_unique_refs r
                    WHERE s."CardReference" = r."CardReference" AND s."IsDistributed" = false;

                    RAISE WARNING 'UniqueCardStock reconciliation (Equinox imports): % distinct imported unique reference(s) checked, % were still flagged IsDistributed=false and have been corrected (%), % have no matching UniqueCardStock row at all (orphans, unfixable by this migration).',
                        total_refs, stale_count,
                        CASE WHEN total_refs = 0 THEN '0%'
                             ELSE round((stale_count::numeric / total_refs) * 100, 2) || '%' END,
                        orphan_count;

                    INSERT INTO "UniqueCardStockReconciliationReport"
                        ("Source", "TotalReferencesChecked", "StaleReferencesFixed", "OrphanReferences")
                    VALUES ('EquinoxImport', total_refs, stale_count, orphan_count);
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not meaningfully reversible: once IsDistributed flips back to true for a
            // stale row, nothing records which rows this migration touched versus which
            // were already correct or got flipped by a real booster open afterward — see
            // SeedUniqueCardStock's Down() for the same tradeoff. Only the report table
            // (this migration's own addition) is safe to remove.
            migrationBuilder.Sql("""DROP TABLE IF EXISTS "UniqueCardStockReconciliationReport";""");
        }
    }
}
