using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlteredOwnership.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedUniqueCardStock : Migration
    {
        private const string ResourceName = "AlteredOwnership.Server.Data.Seed.UniqueCardStock.seed.csv.gz";
        private const int BatchSize = 2000;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var sql in BuildInsertBatches())
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back is a coarse "undo the seed", not a precise inverse: it drops
            // every row, including any real usage flipped to distributed since this
            // migration applied.
            migrationBuilder.Sql("DELETE FROM \"UniqueCardStock\";");
        }

        // Frozen, one-time seed — uniques never change once printed — baked in from
        // CardsData's per-set UniquePrints.csv exports (see
        // Data/Seed/UniqueCardStock.seed.csv.gz, embedded as a resource so it ships
        // inside the migrations bundle with no extra deploy step). Batches into
        // multi-row INSERTs rather than one statement per row so applying this
        // migration (~5.4M rows) stays reasonably fast.
        private static IEnumerable<string> BuildInsertBatches()
        {
            using var resource = typeof(SeedUniqueCardStock).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
            using var gzip = new GZipStream(resource, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);

            reader.ReadLine(); // header: Reference,Set,Faction,IsPublic

            var sql = new StringBuilder();
            var rowsInBatch = 0;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                var fields = line.Split(',');

                if (rowsInBatch == 0)
                {
                    sql.Clear();
                    sql.Append(
                        "INSERT INTO \"UniqueCardStock\" (\"CardReference\", \"Set\", \"Faction\", \"IsDistributed\") VALUES ");
                }
                else
                {
                    sql.Append(", ");
                }

                sql.Append('(').Append('\'').Append(Escape(fields[0])).Append("', '")
                    .Append(Escape(fields[1])).Append("', '")
                    .Append(Escape(fields[2])).Append("', ")
                    .Append(fields[3] == "1" ? "true" : "false").Append(')');
                rowsInBatch++;

                if (rowsInBatch == BatchSize)
                {
                    sql.Append(';');
                    yield return sql.ToString();
                    rowsInBatch = 0;
                }
            }

            if (rowsInBatch > 0)
            {
                sql.Append(';');
                yield return sql.ToString();
            }
        }

        private static string Escape(string value) => value.Replace("'", "''");
    }
}
