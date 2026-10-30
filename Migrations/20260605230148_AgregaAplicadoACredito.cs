using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CajaAhorro.Migrations
{
    /// <inheritdoc />
    public partial class AgregaAplicadoACredito : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AplicadoACredito",
                table: "RetencionLineas",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AplicadoACredito",
                table: "RetencionLineas");
        }
    }
}
