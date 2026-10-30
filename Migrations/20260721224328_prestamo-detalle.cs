using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CajaAhorro.Migrations
{
    /// <inheritdoc />
    public partial class prestamodetalle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MontoAhorroFiniquito",
                table: "Prestamos",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UsarAhorroParaFiniquito",
                table: "Prestamos",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Observaciones",
                table: "AmortizacionDetalles",
                type: "nvarchar(300)",
                maxLength: 300,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MontoAhorroFiniquito",
                table: "Prestamos");

            migrationBuilder.DropColumn(
                name: "UsarAhorroParaFiniquito",
                table: "Prestamos");

            migrationBuilder.DropColumn(
                name: "Observaciones",
                table: "AmortizacionDetalles");
        }
    }
}
