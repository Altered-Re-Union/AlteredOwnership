using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlteredOwnership.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCardOwnershipQuantityNonNegativeConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_CardOwnerships_QuantityNonNegative",
                table: "CardOwnerships",
                sql: "\"Quantity\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CardOwnerships_QuantityNonNegative",
                table: "CardOwnerships");
        }
    }
}
