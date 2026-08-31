using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlteredOwnership.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCardArtCatalogAndPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CardArtCatalog",
                columns: table => new
                {
                    Reference = table.Column<string>(type: "text", nullable: false),
                    FamilyId = table.Column<int>(type: "integer", nullable: false),
                    FamilyName = table.Column<string>(type: "jsonb", nullable: false),
                    CardType = table.Column<string>(type: "text", nullable: false),
                    Faction = table.Column<string>(type: "text", nullable: false),
                    Rarity = table.Column<string>(type: "text", nullable: false),
                    Set = table.Column<string>(type: "text", nullable: false),
                    IsPromo = table.Column<bool>(type: "boolean", nullable: false),
                    IsBaseSet = table.Column<bool>(type: "boolean", nullable: false),
                    MainCost = table.Column<int>(type: "integer", nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardArtCatalog", x => x.Reference);
                });

            migrationBuilder.CreateTable(
                name: "UserCardArtPreferences",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    FamilyId = table.Column<int>(type: "integer", nullable: false),
                    Faction = table.Column<string>(type: "text", nullable: false),
                    Rarity = table.Column<string>(type: "text", nullable: false),
                    SlotIndex = table.Column<int>(type: "integer", nullable: false),
                    PreferredReference = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserCardArtPreferences", x => new { x.UserId, x.FamilyId, x.Faction, x.Rarity, x.SlotIndex });
                });

            migrationBuilder.CreateIndex(
                name: "IX_CardArtCatalog_FamilyId_Faction_Rarity",
                table: "CardArtCatalog",
                columns: new[] { "FamilyId", "Faction", "Rarity" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CardArtCatalog");

            migrationBuilder.DropTable(
                name: "UserCardArtPreferences");
        }
    }
}
