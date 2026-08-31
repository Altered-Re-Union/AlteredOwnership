using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlteredOwnership.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedCardArtCatalog : Migration
    {
        private const string ResourceName = "AlteredOwnership.Server.Data.Seed.CardArtCatalog.seed.csv.gz";
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
            migrationBuilder.Sql("DELETE FROM \"CardArtCatalog\";");
        }

        // Baked in from CardsData's CardFamilies.csv + Sets.csv + per-set
        // NonUniquePrints.csv (see Data/Seed/CardArtCatalog.seed.csv.gz, embedded as a
        // resource like UniqueCardStock's seed). Columns: Reference, FamilyId, CardType,
        // Faction, Rarity, Set, IsPromo, MainCost, SortOrder, Name_en, Name_fr, Name_es,
        // Name_de, Name_it, IsBaseSet. Names are quoted CSV (they can contain commas,
        // e.g. "Nyala, Gifted Conjurer"), so this reads fields with ParseCsvLine rather
        // than a naive Split — unlike SeedUniqueCardStock's plain Reference/Set/Faction
        // columns.
        private static IEnumerable<string> BuildInsertBatches()
        {
            using var resource = typeof(SeedCardArtCatalog).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{ResourceName}' not found.");
            using var gzip = new GZipStream(resource, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip);

            reader.ReadLine(); // header

            var sql = new StringBuilder();
            var rowsInBatch = 0;
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;
                var f = ParseCsvLine(line);

                var reference = f[0];
                var familyId = f[1];
                var cardType = f[2];
                var faction = f[3];
                var rarity = f[4];
                var set = f[5];
                var isPromo = f[6] == "1" ? "true" : "false";
                var mainCost = string.IsNullOrEmpty(f[7]) ? "NULL" : f[7];
                var sortOrder = f[8];
                var isBaseSet = f[14] == "1" ? "true" : "false";
                var familyName = JsonSerializer.Serialize(new Dictionary<string, string>
                {
                    ["en"] = f[9],
                    ["fr"] = f[10],
                    ["es"] = f[11],
                    ["de"] = f[12],
                    ["it"] = f[13],
                });

                if (rowsInBatch == 0)
                {
                    sql.Clear();
                    sql.Append(
                        "INSERT INTO \"CardArtCatalog\" (\"Reference\", \"FamilyId\", \"FamilyName\", \"CardType\", \"Faction\", \"Rarity\", \"Set\", \"IsPromo\", \"IsBaseSet\", \"MainCost\", \"SortOrder\") VALUES ");
                }
                else
                {
                    sql.Append(", ");
                }

                sql.Append('(').Append('\'').Append(Escape(reference)).Append("', ")
                    .Append(familyId).Append(", '")
                    .Append(Escape(familyName)).Append("'::jsonb, '")
                    .Append(Escape(cardType)).Append("', '")
                    .Append(Escape(faction)).Append("', '")
                    .Append(Escape(rarity)).Append("', '")
                    .Append(Escape(set)).Append("', ")
                    .Append(isPromo).Append(", ")
                    .Append(isBaseSet).Append(", ")
                    .Append(mainCost).Append(", ")
                    .Append(sortOrder).Append(')');
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

        // Minimal RFC4180 field splitter for the exact quoting style csv.writer(QUOTE_MINIMAL)
        // produces: a field is only wrapped in double quotes when it needs to be, and an
        // embedded quote is doubled.
        private static string[] ParseCsvLine(string line)
        {
            var fields = new List<string>();
            var field = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            inQuotes = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }
                }
                else if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(field.ToString());
                    field.Clear();
                }
                else
                {
                    field.Append(c);
                }
            }

            fields.Add(field.ToString());
            return fields.ToArray();
        }

        private static string Escape(string value) => value.Replace("'", "''");
    }
}
