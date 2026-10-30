using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CajaAhorro.Migrations
{
    /// <inheritdoc />
    public partial class inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AhorroEncabezados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Quincena = table.Column<int>(type: "int", nullable: false),
                    Anio = table.Column<int>(type: "int", nullable: false),
                    Ciclo = table.Column<int>(type: "int", nullable: false),
                    NombreArchivo = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    TituloOriginal = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    TotalRegistros = table.Column<int>(type: "int", nullable: false),
                    SociosEncontrados = table.Column<int>(type: "int", nullable: false),
                    SociosNoEncontrados = table.Column<int>(type: "int", nullable: false),
                    ImporteTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VecesImportado = table.Column<int>(type: "int", nullable: false),
                    FechaCarga = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaUltimaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AhorroEncabezados", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PagoQuincenalEncabezados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Quincena = table.Column<int>(type: "int", nullable: false),
                    Anio = table.Column<int>(type: "int", nullable: false),
                    NombreArchivo = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Formato = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TotalLineas = table.Column<int>(type: "int", nullable: false),
                    CreditosAplicados = table.Column<int>(type: "int", nullable: false),
                    AhorrosAplicados = table.Column<int>(type: "int", nullable: false),
                    ImporteCreditos = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ImporteAhorros = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    VecesImportado = table.Column<int>(type: "int", nullable: false),
                    FechaCarga = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaUltimaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PagoQuincenalEncabezados", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RetencionEncabezados",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Quincena = table.Column<int>(type: "int", nullable: false),
                    Anio = table.Column<int>(type: "int", nullable: false),
                    Fuente = table.Column<int>(type: "int", nullable: false),
                    TituloOriginal = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    NombreArchivo = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    TotalRegistros = table.Column<int>(type: "int", nullable: false),
                    ImporteTotal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SociosEncontrados = table.Column<int>(type: "int", nullable: false),
                    SociosNuevos = table.Column<int>(type: "int", nullable: false),
                    FechaCarga = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaUltimaActualizacion = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VecesImportado = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetencionEncabezados", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Socios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Nombre = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RFC = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    Adscripcion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    InstitucionActual = table.Column<int>(type: "int", nullable: false),
                    TipoNombramiento = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    JornadaLaboral = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Calle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Numero = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Colonia = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Municipio = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Localidad = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CodigoPostal = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    TelefonoFijo = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    TelefonoCelular = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: false),
                    CorreoElectronico = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    CantidadSolicitada = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Activo = table.Column<bool>(type: "bit", nullable: false),
                    FechaRegistro = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EstadoCaptura = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Socios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Ahorros",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SocioId = table.Column<int>(type: "int", nullable: false),
                    Quincena = table.Column<int>(type: "int", nullable: false),
                    Anio = table.Column<int>(type: "int", nullable: false),
                    Ciclo = table.Column<int>(type: "int", nullable: false),
                    MontoRetenido = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MontoEsperado = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    FechaRegistro = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AhorroEncabezadoId = table.Column<int>(type: "int", nullable: true),
                    PagoQuincenalId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ahorros", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Ahorros_AhorroEncabezados_AhorroEncabezadoId",
                        column: x => x.AhorroEncabezadoId,
                        principalTable: "AhorroEncabezados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Ahorros_PagoQuincenalEncabezados_PagoQuincenalId",
                        column: x => x.PagoQuincenalId,
                        principalTable: "PagoQuincenalEncabezados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Ahorros_Socios_SocioId",
                        column: x => x.SocioId,
                        principalTable: "Socios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Prestamos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SocioId = table.Column<int>(type: "int", nullable: false),
                    TipoPrestamo = table.Column<int>(type: "int", nullable: false),
                    ImporteSolicitado = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ImporteOtorgado = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TasaInteres = table.Column<decimal>(type: "decimal(6,4)", nullable: false),
                    NumeroDescuentos = table.Column<int>(type: "int", nullable: false),
                    PagoQuincenal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalAPagar = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    QuincenaInicial = table.Column<int>(type: "int", nullable: false),
                    AnioInicial = table.Column<int>(type: "int", nullable: false),
                    QuincenaFinal = table.Column<int>(type: "int", nullable: false),
                    AnioFinal = table.Column<int>(type: "int", nullable: false),
                    FechaSolicitud = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EsTransferencia = table.Column<bool>(type: "bit", nullable: false),
                    NumeroChequeFolio = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    FechaTransferencia = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ImporteChequeFolio = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Observaciones = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    FechaRegistro = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prestamos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Prestamos_Socios_SocioId",
                        column: x => x.SocioId,
                        principalTable: "Socios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RetencionLineas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RetencionEncabezadoId = table.Column<int>(type: "int", nullable: false),
                    RFC = table.Column<string>(type: "nvarchar(13)", maxLength: 13, nullable: false),
                    NombreEmpleado = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Importe = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    UnidadResponsable = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Concepto = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    EsSocio = table.Column<bool>(type: "bit", nullable: false),
                    SocioId = table.Column<int>(type: "int", nullable: true),
                    EsMigracion = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetencionLineas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RetencionLineas_RetencionEncabezados_RetencionEncabezadoId",
                        column: x => x.RetencionEncabezadoId,
                        principalTable: "RetencionEncabezados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RetencionLineas_Socios_SocioId",
                        column: x => x.SocioId,
                        principalTable: "Socios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "SocioInstituciones",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SocioId = table.Column<int>(type: "int", nullable: false),
                    Institucion = table.Column<int>(type: "int", nullable: false),
                    QuincenaInicio = table.Column<int>(type: "int", nullable: false),
                    AnioInicio = table.Column<int>(type: "int", nullable: false),
                    QuincenaFin = table.Column<int>(type: "int", nullable: true),
                    AnioFin = table.Column<int>(type: "int", nullable: true),
                    Observaciones = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    FechaRegistro = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocioInstituciones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocioInstituciones_Socios_SocioId",
                        column: x => x.SocioId,
                        principalTable: "Socios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AmortizacionDetalles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PrestamoId = table.Column<int>(type: "int", nullable: false),
                    NumeroPago = table.Column<int>(type: "int", nullable: false),
                    Quincena = table.Column<int>(type: "int", nullable: false),
                    Anio = table.Column<int>(type: "int", nullable: false),
                    SaldoInicial = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Interes = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Capital = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Pago = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SaldoFinal = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    MontoAplicado = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    StatusPago = table.Column<int>(type: "int", nullable: false),
                    PagoQuincenalId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AmortizacionDetalles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AmortizacionDetalles_PagoQuincenalEncabezados_PagoQuincenalId",
                        column: x => x.PagoQuincenalId,
                        principalTable: "PagoQuincenalEncabezados",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AmortizacionDetalles_Prestamos_PrestamoId",
                        column: x => x.PrestamoId,
                        principalTable: "Prestamos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AhorroEncabezados_Quincena_Anio_Ciclo",
                table: "AhorroEncabezados",
                columns: new[] { "Quincena", "Anio", "Ciclo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Ahorros_AhorroEncabezadoId",
                table: "Ahorros",
                column: "AhorroEncabezadoId");

            migrationBuilder.CreateIndex(
                name: "IX_Ahorros_PagoQuincenalId",
                table: "Ahorros",
                column: "PagoQuincenalId");

            migrationBuilder.CreateIndex(
                name: "IX_Ahorros_SocioId_Quincena_Anio_Ciclo",
                table: "Ahorros",
                columns: new[] { "SocioId", "Quincena", "Anio", "Ciclo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AmortizacionDetalles_PagoQuincenalId",
                table: "AmortizacionDetalles",
                column: "PagoQuincenalId");

            migrationBuilder.CreateIndex(
                name: "IX_AmortizacionDetalles_PrestamoId_NumeroPago",
                table: "AmortizacionDetalles",
                columns: new[] { "PrestamoId", "NumeroPago" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Prestamos_SocioId_Status",
                table: "Prestamos",
                columns: new[] { "SocioId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RetencionEncabezados_Quincena_Anio_Fuente",
                table: "RetencionEncabezados",
                columns: new[] { "Quincena", "Anio", "Fuente" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RetencionLineas_RetencionEncabezadoId_RFC",
                table: "RetencionLineas",
                columns: new[] { "RetencionEncabezadoId", "RFC" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RetencionLineas_SocioId",
                table: "RetencionLineas",
                column: "SocioId");

            migrationBuilder.CreateIndex(
                name: "IX_SocioInstituciones_SocioId_AnioInicio_QuincenaInicio",
                table: "SocioInstituciones",
                columns: new[] { "SocioId", "AnioInicio", "QuincenaInicio" });

            migrationBuilder.CreateIndex(
                name: "IX_Socios_RFC",
                table: "Socios",
                column: "RFC",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Ahorros");

            migrationBuilder.DropTable(
                name: "AmortizacionDetalles");

            migrationBuilder.DropTable(
                name: "RetencionLineas");

            migrationBuilder.DropTable(
                name: "SocioInstituciones");

            migrationBuilder.DropTable(
                name: "AhorroEncabezados");

            migrationBuilder.DropTable(
                name: "PagoQuincenalEncabezados");

            migrationBuilder.DropTable(
                name: "Prestamos");

            migrationBuilder.DropTable(
                name: "RetencionEncabezados");

            migrationBuilder.DropTable(
                name: "Socios");
        }
    }
}
