using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CajaAhorro.Migrations
{
    /// <inheritdoc />
    public partial class _10 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "AportacionAhorroInicial",
                table: "Prestamos",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AportacionAhorroInicial",
                table: "Prestamos");
        }
    }
}
