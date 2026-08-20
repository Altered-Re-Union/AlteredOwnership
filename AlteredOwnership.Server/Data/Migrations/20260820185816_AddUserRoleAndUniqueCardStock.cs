using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlteredOwnership.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRoleAndUniqueCardStock : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "Player");

            migrationBuilder.CreateTable(
                name: "UniqueCardStock",
                columns: table => new
                {
                    CardReference = table.Column<string>(type: "text", nullable: false),
                    Set = table.Column<string>(type: "text", nullable: false),
                    Faction = table.Column<string>(type: "text", nullable: false),
                    IsDistributed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UniqueCardStock", x => x.CardReference);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UniqueCardStock_Set_IsDistributed",
                table: "UniqueCardStock",
                columns: new[] { "Set", "IsDistributed" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UniqueCardStock");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "Users");
        }
    }
}
