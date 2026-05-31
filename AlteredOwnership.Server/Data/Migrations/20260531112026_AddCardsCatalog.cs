using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlteredOwnership.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCardsCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Cards",
                columns: table => new
                {
                    Reference = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "jsonb", nullable: false),
                    ImagePath = table.Column<string>(type: "jsonb", nullable: false),
                    Set = table.Column<string>(type: "text", nullable: false),
                    Faction = table.Column<string>(type: "text", nullable: false),
                    Rarity = table.Column<string>(type: "text", nullable: false),
                    CardType = table.Column<string>(type: "text", nullable: false),
                    Variation = table.Column<string>(type: "text", nullable: false),
                    SubTypes = table.Column<List<string>>(type: "text[]", nullable: false),
                    IsBanned = table.Column<bool>(type: "boolean", nullable: false),
                    IsSuspended = table.Column<bool>(type: "boolean", nullable: false),
                    MainCost = table.Column<int>(type: "integer", nullable: true),
                    RecallCost = table.Column<int>(type: "integer", nullable: true),
                    Forest = table.Column<int>(type: "integer", nullable: true),
                    Mountain = table.Column<int>(type: "integer", nullable: true),
                    Ocean = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cards", x => x.Reference);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Cards_CardType",
                table: "Cards",
                column: "CardType");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_Faction",
                table: "Cards",
                column: "Faction");

            migrationBuilder.CreateIndex(
                name: "IX_Cards_Rarity",
                table: "Cards",
                column: "Rarity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cards");
        }
    }
}
