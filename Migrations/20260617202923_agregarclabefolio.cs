using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CajaAhorro.Migrations
{
    /// <inheritdoc />
    public partial class agregarclabefolio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClabeInterbancaria",
                table: "Socios",
                type: "nvarchar(18)",
                maxLength: 18,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "CapitalLiquidadoReestructura",
                table: "Prestamos",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Folio",
                table: "Prestamos",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PrestamoAnteriorId",
                table: "Prestamos",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Configuraciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FechaRetornoAhorroEscolar = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaRetornoAhorroNavideno = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PorcentajeDividendos = table.Column<decimal>(type: "decimal(7,4)", nullable: false),
                    FechaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Configuraciones", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Prestamos_Folio",
                table: "Prestamos",
                column: "Folio",
                unique: true,
                filter: "[Folio] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Prestamos_PrestamoAnteriorId",
                table: "Prestamos",
                column: "PrestamoAnteriorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Prestamos_Prestamos_PrestamoAnteriorId",
                table: "Prestamos",
                column: "PrestamoAnteriorId",
                principalTable: "Prestamos",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Prestamos_Prestamos_PrestamoAnteriorId",
                table: "Prestamos");

            migrationBuilder.DropTable(
                name: "Configuraciones");

            migrationBuilder.DropIndex(
                name: "IX_Prestamos_Folio",
                table: "Prestamos");

            migrationBuilder.DropIndex(
                name: "IX_Prestamos_PrestamoAnteriorId",
                table: "Prestamos");

            migrationBuilder.DropColumn(
                name: "ClabeInterbancaria",
                table: "Socios");

            migrationBuilder.DropColumn(
                name: "CapitalLiquidadoReestructura",
                table: "Prestamos");

            migrationBuilder.DropColumn(
                name: "Folio",
                table: "Prestamos");

            migrationBuilder.DropColumn(
                name: "PrestamoAnteriorId",
                table: "Prestamos");
        }
    }
}
