using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlteredOwnership.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueCardStockRandomKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UniqueCardStock_Set_IsDistributed",
                table: "UniqueCardStock");

            migrationBuilder.AddColumn<double>(
                name: "RandomKey",
                table: "UniqueCardStock",
                type: "double precision",
                nullable: false,
                defaultValueSql: "random()");

            migrationBuilder.CreateIndex(
                name: "IX_UniqueCardStock_Faction_IsDistributed_RandomKey",
                table: "UniqueCardStock",
                columns: new[] { "Faction", "IsDistributed", "RandomKey" });

            migrationBuilder.CreateIndex(
                name: "IX_UniqueCardStock_IsDistributed_RandomKey",
                table: "UniqueCardStock",
                columns: new[] { "IsDistributed", "RandomKey" });

            migrationBuilder.CreateIndex(
                name: "IX_UniqueCardStock_Set_IsDistributed_RandomKey",
                table: "UniqueCardStock",
                columns: new[] { "Set", "IsDistributed", "RandomKey" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UniqueCardStock_Faction_IsDistributed_RandomKey",
                table: "UniqueCardStock");

            migrationBuilder.DropIndex(
                name: "IX_UniqueCardStock_IsDistributed_RandomKey",
                table: "UniqueCardStock");

            migrationBuilder.DropIndex(
                name: "IX_UniqueCardStock_Set_IsDistributed_RandomKey",
                table: "UniqueCardStock");

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
