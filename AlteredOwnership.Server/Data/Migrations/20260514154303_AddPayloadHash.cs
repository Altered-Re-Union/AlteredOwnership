using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlteredOwnership.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPayloadHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PayloadHash",
                table: "OwnershipEvents",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OwnershipEvents_PayloadHash",
                table: "OwnershipEvents",
                column: "PayloadHash",
                unique: true,
                filter: "\"PayloadHash\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OwnershipEvents_PayloadHash",
                table: "OwnershipEvents");

            migrationBuilder.DropColumn(
                name: "PayloadHash",
                table: "OwnershipEvents");
        }
    }
}
