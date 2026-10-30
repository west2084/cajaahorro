using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using CajaAhorro.Data;
using CajaAhorro.Models;

namespace CajaAhorro.Services;

/// <summary>
/// Genera reportes en Excel (ClosedXML):
///   1. Créditos otorgados
///   2. Socios con datos faltantes (RFC, contacto, domicilio…)
///   3. Ahorradores por ciclo
/// </summary>
public class ReporteService
{
    private readonly AppDbContext _db;

    public ReporteService(AppDbContext db) => _db = db;

    private const string FormatoMoneda = "$ #,##0.00";

    // ═════════════════════════════════════════════════════════════════════
    // 1. CRÉDITOS OTORGADOS
    // ═════════════════════════════════════════════════════════════════════

    public async Task<byte[]> GenerarReporteCreditosAsync(int? anio, StatusPrestamo? status)
    {
        var query = _db.Prestamos
            .Include(p => p.Socio)
            .AsNoTracking()
            .AsQueryable();

        if (anio.HasValue)
            query = query.Where(p => p.AnioInicial == anio.Value);

        if (status.HasValue)
            query = query.Where(p => p.Status == status.Value);

        var prestamos = await query
            .OrderBy(p => p.AnioInicial).ThenBy(p => p.QuincenaInicial)
            .ThenBy(p => p.Socio.Nombre)
            .ToListAsync();

        // Saldo pendiente y quincenas pagadas desde la amortización (misma
        // lógica que el dashboard: mínimo SaldoInicial de los pagos pendientes)
        var ids = prestamos.Select(p => p.Id).ToList();

        var amortInfo = await _db.AmortizacionDetalles
            .AsNoTracking()
            .Where(a => ids.Contains(a.PrestamoId))
            .GroupBy(a => a.PrestamoId)
            .Select(g => new
            {
                PrestamoId = g.Key,
                SaldoPendiente = g.Where(a => a.StatusPago == StatusPago.Pendiente)
                                  .Min(a => (decimal?)a.SaldoInicial) ?? 0m,
                QuincenasPagadas = g.Count(a => a.StatusPago == StatusPago.Aplicado
                                             || a.StatusPago == StatusPago.Diferencia),
                TotalAplicado = g.Sum(a => a.MontoAplicado ?? 0m),
            })
            .ToDictionaryAsync(x => x.PrestamoId);

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Créditos");

        // Título
        var filtro = $"{(anio.HasValue ? $"Año {anio}" : "Todos los años")}" +
                     $" · {(status.HasValue ? StatusTexto(status.Value) : "Todos los status")}";

        ws.Cell(1, 1).Value = "CAJA DE AHORRO SNTSA SECCIÓN 39 — CRÉDITOS OTORGADOS";
        ws.Cell(2, 1).Value = $"Filtro: {filtro} · Generado: {DateTime.Now:dd/MM/yyyy HH:mm}";
        ws.Range(1, 1, 1, 16).Merge().Style
            .Font.SetBold().Font.SetFontSize(13);
        ws.Range(2, 1, 2, 16).Merge().Style
            .Font.SetFontColor(XLColor.Gray).Font.SetFontSize(10);

        // Encabezados
        var headers = new[]
        {
            "#", "Socio", "RFC", "Institución", "Tipo", "Fecha solicitud",
            "QNA inicio", "QNA fin", "Plazo (qnas)", "Tasa",
            "Importe otorgado", "Pago quincenal", "Total a pagar",
            "QNAs pagadas", "Saldo pendiente", "Status"
        };

        const int filaHeader = 4;
        for (int c = 0; c < headers.Length; c++)
            ws.Cell(filaHeader, c + 1).Value = headers[c];

        EstiloHeader(ws.Range(filaHeader, 1, filaHeader, headers.Length));

        // Datos
        int fila = filaHeader + 1;
        int consecutivo = 1;

        foreach (var p in prestamos)
        {
            amortInfo.TryGetValue(p.Id, out var info);

            // Si no hay amortización: activos deben saldo completo, los demás 0
            decimal saldoPendiente = info?.SaldoPendiente
                ?? (p.Status == StatusPrestamo.Activo ? p.ImporteOtorgado : 0m);

            ws.Cell(fila, 1).Value  = consecutivo++;
            ws.Cell(fila, 2).Value  = p.Socio?.Nombre ?? "—";
            ws.Cell(fila, 3).Value  = p.Socio?.RFC ?? "—";
            ws.Cell(fila, 4).Value  = p.Socio?.InstitucionVigente() == InstitucionTipo.Federal
                                        ? "IMSS Bienestar" : "SSZ";
            ws.Cell(fila, 5).Value  = p.TipoPrestamo == TipoPrestamo.Reestructura
                                        ? "Reestructura" : "Ordinario";
            ws.Cell(fila, 6).Value  = p.FechaSolicitud;
            ws.Cell(fila, 6).Style.DateFormat.Format = "dd/mm/yyyy";
            ws.Cell(fila, 7).Value  = $"{p.QuincenaInicial}/{p.AnioInicial}";
            ws.Cell(fila, 8).Value  = $"{p.QuincenaFinal}/{p.AnioFinal}";
            ws.Cell(fila, 9).Value  = p.NumeroDescuentos;
            ws.Cell(fila, 10).Value = p.TasaInteres;
            ws.Cell(fila, 10).Style.NumberFormat.Format = "0.0%";
            ws.Cell(fila, 11).Value = p.ImporteOtorgado;
            ws.Cell(fila, 12).Value = p.PagoQuincenal;
            ws.Cell(fila, 13).Value = p.TotalAPagar;
            ws.Cell(fila, 14).Value = info?.QuincenasPagadas ?? 0;
            ws.Cell(fila, 15).Value = saldoPendiente;
            ws.Cell(fila, 16).Value = StatusTexto(p.Status);

            fila++;
        }

        // Formato moneda
        ws.Range(filaHeader + 1, 11, fila - 1, 13).Style.NumberFormat.Format = FormatoMoneda;
        ws.Range(filaHeader + 1, 15, fila - 1, 15).Style.NumberFormat.Format = FormatoMoneda;

        // Totales
        if (prestamos.Any())
        {
            ws.Cell(fila, 2).Value  = $"TOTAL ({prestamos.Count} créditos)";
            ws.Cell(fila, 11).FormulaA1 = $"SUM(K{filaHeader + 1}:K{fila - 1})";
            ws.Cell(fila, 12).FormulaA1 = $"SUM(L{filaHeader + 1}:L{fila - 1})";
            ws.Cell(fila, 13).FormulaA1 = $"SUM(M{filaHeader + 1}:M{fila - 1})";
            ws.Cell(fila, 15).FormulaA1 = $"SUM(O{filaHeader + 1}:O{fila - 1})";
            ws.Range(fila, 1, fila, 16).Style
                .Font.SetBold()
                .Fill.SetBackgroundColor(XLColor.FromHtml("#f3f4f6"));
            ws.Range(fila, 11, fila, 15).Style.NumberFormat.Format = FormatoMoneda;
        }

        FinalizarHoja(ws, filaHeader, headers.Length);
        return ABytes(wb);
    }

    // ═════════════════════════════════════════════════════════════════════
    // 2. SOCIOS CON DATOS FALTANTES
    // ═════════════════════════════════════════════════════════════════════

    public async Task<byte[]> GenerarReporteSociosIncompletosAsync()
    {
        var socios = await _db.Socios
            .AsNoTracking()
            .Where(s => s.Activo)
            .OrderByDescending(s => s.EstadoCaptura)   // Provisionales primero
            .ThenBy(s => s.Nombre)
            .ToListAsync();

        var incompletos = socios
            .Select(s => new { Socio = s, Faltantes = CamposFaltantes(s) })
            .Where(x => x.Faltantes.Count > 0)
            .ToList();

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Datos faltantes");

        ws.Cell(1, 1).Value = "CAJA DE AHORRO SNTSA SECCIÓN 39 — SOCIOS CON DATOS FALTANTES";
        ws.Cell(2, 1).Value = $"Socios activos revisados: {socios.Count} · " +
                              $"Con datos faltantes: {incompletos.Count} · " +
                              $"Generado: {DateTime.Now:dd/MM/yyyy HH:mm}";
        ws.Range(1, 1, 1, 13).Merge().Style.Font.SetBold().Font.SetFontSize(13);
        ws.Range(2, 1, 2, 13).Merge().Style.Font.SetFontColor(XLColor.Gray).Font.SetFontSize(10);

        var headers = new[]
        {
            "#", "Nombre", "RFC", "CURP", "Estado captura",
            "RFC válido", "Adscripción", "Nombramiento", "Teléfono",
            "Correo", "Domicilio", "Total faltantes", "Detalle"
        };

        const int filaHeader = 4;
        for (int c = 0; c < headers.Length; c++)
            ws.Cell(filaHeader, c + 1).Value = headers[c];
        EstiloHeader(ws.Range(filaHeader, 1, filaHeader, headers.Length));

        int fila = filaHeader + 1;
        int consecutivo = 1;

        foreach (var x in incompletos)
        {
            var s = x.Socio;
            var f = x.Faltantes;

            ws.Cell(fila, 1).Value = consecutivo++;
            ws.Cell(fila, 2).Value = string.IsNullOrWhiteSpace(s.Nombre) ? "(SIN NOMBRE)" : s.Nombre;
            ws.Cell(fila, 3).Value = string.IsNullOrWhiteSpace(s.RFC) ? "(SIN RFC)" : s.RFC;
            ws.Cell(fila, 4).Value = string.IsNullOrWhiteSpace(s.CURP) ? "—" : s.CURP;
            ws.Cell(fila, 5).Value = s.EstadoCaptura == EstadoCaptura.Provisional
                                        ? "PROVISIONAL" : "Completo";

            // Marcas: ✔ tiene el dato, ✘ falta
            Marca(ws.Cell(fila, 6),  !f.Contains("RFC"));
            Marca(ws.Cell(fila, 7),  !f.Contains("Adscripción"));
            Marca(ws.Cell(fila, 8),  !f.Contains("Nombramiento"));
            Marca(ws.Cell(fila, 9),  !f.Contains("Teléfono"));
            Marca(ws.Cell(fila, 10), !f.Contains("Correo"));
            Marca(ws.Cell(fila, 11), !f.Contains("Domicilio"));

            ws.Cell(fila, 12).Value = f.Count;
            ws.Cell(fila, 13).Value = string.Join(", ", f);

            // Resaltar provisionales
            if (s.EstadoCaptura == EstadoCaptura.Provisional)
                ws.Range(fila, 1, fila, headers.Length).Style
                  .Fill.SetBackgroundColor(XLColor.FromHtml("#fef9c3"));

            fila++;
        }

        FinalizarHoja(ws, filaHeader, headers.Length);
        return ABytes(wb);
    }

    /// <summary>Lista de campos faltantes o inválidos del socio.</summary>
    private static List<string> CamposFaltantes(Socio s)
    {
        var faltantes = new List<string>();

        // RFC: vacío o con longitud inválida (12 = moral, 13 = física)
        var rfc = s.RFC?.Trim() ?? "";
        if (rfc.Length < 12 || rfc.Length > 13)
            faltantes.Add("RFC");

        if (string.IsNullOrWhiteSpace(s.Nombre))
            faltantes.Add("Nombre");

        if (string.IsNullOrWhiteSpace(s.Adscripcion))
            faltantes.Add("Adscripción");

        if (string.IsNullOrWhiteSpace(s.TipoNombramiento))
            faltantes.Add("Nombramiento");

        if (string.IsNullOrWhiteSpace(s.TelefonoCelular) &&
            string.IsNullOrWhiteSpace(s.TelefonoFijo))
            faltantes.Add("Teléfono");

        if (string.IsNullOrWhiteSpace(s.CorreoElectronico))
            faltantes.Add("Correo");

        if (string.IsNullOrWhiteSpace(s.Calle) ||
            string.IsNullOrWhiteSpace(s.Municipio))
            faltantes.Add("Domicilio");

        return faltantes;
    }

    // ═════════════════════════════════════════════════════════════════════
    // 3. AHORRADORES
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Reporte de ahorradores. <paramref name="anio"/> es el año en que EMPIEZA
    /// el ciclo consultado: para Navideño es el año calendario completo
    /// (ene–dic); para Escolar cubre de julio de ese año a junio del
    /// siguiente. Si no se especifica ciclo, se incluyen ambos con su propio
    /// rango (el escolar puede traer quincenas del año siguiente).
    /// </summary>
    public async Task<byte[]> GenerarReporteAhorradoresAsync(int anio, CicloAhorro? ciclo)
    {
        // EF Core no puede traducir una llamada a un método estático arbitrario
        // (PerteneceAlCiclo) a SQL. Se hace un filtro grueso traducible por año
        // (el ciclo escolar solo puede tocar "anio" y "anio+1") y el rango
        // exacto de quincenas se aplica en memoria una vez materializado.
        var query = _db.Ahorros
            .Include(a => a.Socio)
            .AsNoTracking()
            .Where(a => a.Anio == anio || a.Anio == anio + 1)
            .AsQueryable();

        if (ciclo.HasValue)
            query = query.Where(a => a.Ciclo == ciclo.Value);

        var candidatos = await query.ToListAsync();

        var ahorros = ciclo.HasValue
            ? candidatos.Where(a => AhorroImportService.PerteneceAlCiclo(ciclo.Value, anio, a.Anio, a.Quincena)).ToList()
            : candidatos.Where(a =>
                AhorroImportService.PerteneceAlCiclo(CicloAhorro.Navideño, anio, a.Anio, a.Quincena) ||
                AhorroImportService.PerteneceAlCiclo(CicloAhorro.Escolar, anio, a.Anio, a.Quincena))
                .ToList();

        var porSocio = ahorros
            .GroupBy(a => a.SocioId)
            .Select(g =>
            {
                var socio = g.First().Socio;
                var ultima = g.OrderByDescending(a => a.Anio * 100 + a.Quincena).First();
                return new
                {
                    Socio            = socio,
                    Ciclos           = string.Join(" + ", g.Select(a => a.Ciclo)
                                                           .Distinct()
                                                           .OrderBy(c => c)),
                    QuincenasAportadas = g.Count(),
                    TotalAhorrado    = g.Sum(a => a.MontoRetenido),
                    PromedioQna      = g.Average(a => a.MontoRetenido),
                    UltimaQna        = $"{ultima.Quincena}/{ultima.Anio}",
                    UltimoMonto      = ultima.MontoRetenido,
                };
            })
            .OrderBy(x => x.Socio?.Nombre)
            .ToList();

        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Ahorradores");

        var filtro = ciclo switch
        {
            CicloAhorro.Navideño => $"Ciclo Navideño {anio} (ene–dic {anio})",
            CicloAhorro.Escolar  => $"Ciclo Escolar {anio} (jul {anio} – jun {anio + 1})",
            _                    => $"Año {anio} · Ambos ciclos"
        };

        ws.Cell(1, 1).Value = "CAJA DE AHORRO SNTSA SECCIÓN 39 — AHORRADORES";
        ws.Cell(2, 1).Value = $"Filtro: {filtro} · Generado: {DateTime.Now:dd/MM/yyyy HH:mm}";
        ws.Range(1, 1, 1, 11).Merge().Style.Font.SetBold().Font.SetFontSize(13);
        ws.Range(2, 1, 2, 11).Merge().Style.Font.SetFontColor(XLColor.Gray).Font.SetFontSize(10);

        var headers = new[]
        {
            "#", "Socio", "RFC", "CURP", "Institución", "Ciclo(s)",
            "Quincenas aportadas", "Promedio quincenal",
            "Última QNA", "Último monto", "Total ahorrado"
        };

        const int filaHeader = 4;
        for (int c = 0; c < headers.Length; c++)
            ws.Cell(filaHeader, c + 1).Value = headers[c];
        EstiloHeader(ws.Range(filaHeader, 1, filaHeader, headers.Length));

        int fila = filaHeader + 1;
        int consecutivo = 1;

        foreach (var x in porSocio)
        {
            ws.Cell(fila, 1).Value  = consecutivo++;
            ws.Cell(fila, 2).Value  = x.Socio?.Nombre ?? "—";
            ws.Cell(fila, 3).Value  = x.Socio?.RFC ?? "—";
            ws.Cell(fila, 4).Value  = string.IsNullOrWhiteSpace(x.Socio?.CURP) ? "—" : x.Socio!.CURP;
            ws.Cell(fila, 5).Value  = x.Socio?.InstitucionVigente() == InstitucionTipo.Federal
                                        ? "IMSS Bienestar" : "SSZ";
            ws.Cell(fila, 6).Value  = x.Ciclos;
            ws.Cell(fila, 7).Value  = x.QuincenasAportadas;
            ws.Cell(fila, 8).Value  = x.PromedioQna;
            ws.Cell(fila, 9).Value  = x.UltimaQna;
            ws.Cell(fila, 10).Value = x.UltimoMonto;
            ws.Cell(fila, 11).Value = x.TotalAhorrado;
            fila++;
        }

        ws.Range(filaHeader + 1, 8, fila - 1, 8).Style.NumberFormat.Format  = FormatoMoneda;
        ws.Range(filaHeader + 1, 10, fila - 1, 11).Style.NumberFormat.Format = FormatoMoneda;

        if (porSocio.Any())
        {
            ws.Cell(fila, 2).Value = $"TOTAL ({porSocio.Count} ahorradores)";
            ws.Cell(fila, 11).FormulaA1 = $"SUM(K{filaHeader + 1}:K{fila - 1})";
            ws.Range(fila, 1, fila, 11).Style
                .Font.SetBold()
                .Fill.SetBackgroundColor(XLColor.FromHtml("#f3f4f6"));
            ws.Cell(fila, 11).Style.NumberFormat.Format = FormatoMoneda;
        }

        FinalizarHoja(ws, filaHeader, headers.Length);
        return ABytes(wb);
    }

    // ═════════════════════════════════════════════════════════════════════
    // Helpers de formato
    // ═════════════════════════════════════════════════════════════════════

    private static void EstiloHeader(IXLRange rango)
    {
        rango.Style
            .Font.SetBold()
            .Font.SetFontColor(XLColor.White)
            .Fill.SetBackgroundColor(XLColor.FromHtml("#1d4ed8"))
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
    }

    private static void Marca(IXLCell celda, bool tiene)
    {
        celda.Value = tiene ? "✔" : "FALTA";
        celda.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        celda.Style.Font.SetFontColor(tiene
            ? XLColor.FromHtml("#059669")
            : XLColor.FromHtml("#dc2626"));
        if (!tiene) celda.Style.Font.SetBold();
    }

    private static void FinalizarHoja(IXLWorksheet ws, int filaHeader, int columnas)
    {
        var ultimaFila = ws.LastRowUsed()?.RowNumber() ?? filaHeader;

        ws.Range(filaHeader, 1, ultimaFila, columnas)
          .SetAutoFilter();

        ws.SheetView.FreezeRows(filaHeader);
        ws.Columns(1, columnas).AdjustToContents();

        // Evitar columnas excesivamente anchas (nombres largos)
        for (int c = 1; c <= columnas; c++)
            if (ws.Column(c).Width > 45) ws.Column(c).Width = 45;
    }

    private static string StatusTexto(StatusPrestamo s) => s switch
    {
        StatusPrestamo.Activo         => "Activo",
        StatusPrestamo.Liquidado      => "Saldado",
        StatusPrestamo.Cancelado      => "Cancelado",
        StatusPrestamo.Reestructurado => "Reestructurado",
        _                             => s.ToString(),
    };

    private static byte[] ABytes(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
}
