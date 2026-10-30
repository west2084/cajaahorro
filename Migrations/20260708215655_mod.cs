using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CajaAhorro.Migrations
{
    /// <inheritdoc />
    public partial class mod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CURP",
                table: "Socios",
                type: "nvarchar(18)",
                maxLength: 18,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CURP",
                table: "Socios");
        }
    }
}
