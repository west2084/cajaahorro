using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using CajaAhorro.Data;
using CajaAhorro.Models;
using System.Text.RegularExpressions;

namespace CajaAhorro.Services;

public class PagoQuincenalService
{
    private readonly AppDbContext _db;

    public PagoQuincenalService(AppDbContext db) => _db = db;

    // ── Tipos de concepto detectados en el archivo ────────────────────────────

    private enum ConceptoPago { Credito, AhorroSindicato, AhorroOtro, Ignorar }

    // ── DTO de resultado ─────────────────────────────────────────────────────

    public class ResultadoImportacion
    {
        public int Quincena { get; set; }
        public int Anio { get; set; }
        public string FormatoDetectado { get; set; } = "";
        public string PestanaLeida { get; set; } = "";

        public int TotalLineasArchivo { get; set; }
        public int CreditosAplicados { get; set; }
        public int AhorrosAplicados { get; set; }
        public int SociosNoEncontrados { get; set; }
        public int LineasIgnoradas { get; set; }

        public decimal ImporteCreditos { get; set; }
        public decimal ImporteAhorros { get; set; }

        public bool EsReimportacion { get; set; }
        public List<string> Advertencias { get; set; } = new();
        public List<string> Log { get; set; } = new();
        public bool Exitoso { get; set; } = true;
        public string? Error { get; set; }
    }

    // ── Punto de entrada ─────────────────────────────────────────────────────

    /// <summary>
    /// Importa UN archivo Excel de pagos quincenales.
    /// El archivo puede tener formato IMSS BIENESTAR (QNA 01-02) o TTIB (QNA 03+).
    /// <param name="quincenaOverride">Si se indica, se usa esta quincena en lugar de la detectada.</param>
    /// </summary>
    public async Task<ResultadoImportacion> ImportarAsync(
        Stream archivoExcel,
        string nombreArchivo,
        List<string> log,
        int? quincenaOverride = null,
        int? anioOverride = null)
    {
        var res = new ResultadoImportacion { Log = log };

        try
        {
            using var wb = new XLWorkbook(archivoExcel);

            // 1. Encontrar la pestaña correcta (ignora PRESTAMOS TTIB y Hoja1)
            var hoja = EncontrarPestanaQuincena(wb, log);
            if (hoja == null)
            {
                res.Exitoso = false;
                res.Error = "No se encontró ninguna pestaña de quincena válida en el archivo.";
                return res;
            }

            res.PestanaLeida = hoja.Name;
            log.Add($"[INFO] Pestaña seleccionada: «{hoja.Name}»");

            // 2. Detectar formato
            var formato = DetectarFormato(hoja);
            res.FormatoDetectado = formato == "IMSS" ? "IMSS BIENESTAR" : "TTIB";
            log.Add($"[INFO] Formato detectado: {res.FormatoDetectado}");

            // 3. Extraer quincena/año
            var (qna, anio) = ExtraerQuincenaAnio(hoja.Name, log);
            res.Quincena = quincenaOverride ?? qna;
            res.Anio = anioOverride ?? anio;
            log.Add($"[INFO] Quincena: {res.Quincena:D2}/{res.Anio}");

            // 4. Cargar socios de la BD indexados por RFC
            var sociosPorRFC = await _db.Socios
                .ToDictionaryAsync(s => s.RFC.ToUpper().Trim(), StringComparer.OrdinalIgnoreCase);
            log.Add($"[INFO] Socios en BD: {sociosPorRFC.Count}");

            // 5. Leer líneas según formato
            List<LineaPago> lineas;
            if (formato == "IMSS")
                lineas = LeerFormatoIMSS(hoja, log, res);
            else
                lineas = LeerFormatoSSZ_TTIB(hoja, log, res);

            log.Add($"[INFO] Líneas válidas leídas: {lineas.Count} " +
                    $"(créditos: {lineas.Count(l => l.Concepto == ConceptoPago.Credito)}, " +
                    $"ahorros: {lineas.Count(l => l.Concepto != ConceptoPago.Credito && l.Concepto != ConceptoPago.Ignorar)})");

            // 6. Verificar si ya existe importación para esta quincena/año/archivo
            var yaExiste = await _db.PagoQuincenalEncabezados
                .AnyAsync(e => e.Quincena == res.Quincena && e.Anio == res.Anio && e.NombreArchivo == nombreArchivo);
            res.EsReimportacion = yaExiste;
            if (yaExiste)
                log.Add($"[WARN] Este archivo ya fue importado — se ACTUALIZARÁN los registros existentes.");

            // 7. Upsert encabezado
            var encabezado = await _db.PagoQuincenalEncabezados
                .FirstOrDefaultAsync(e => e.Quincena == res.Quincena
                                       && e.Anio == res.Anio
                                       && e.NombreArchivo == nombreArchivo);
            if (encabezado == null)
            {
                encabezado = new PagoQuincenalEncabezado
                {
                    Quincena = res.Quincena,
                    Anio = res.Anio,
                    NombreArchivo = nombreArchivo,
                    Formato = res.FormatoDetectado,
                    FechaCarga = DateTime.Now,
                };
                _db.PagoQuincenalEncabezados.Add(encabezado);
                await _db.SaveChangesAsync();
            }
            else
            {
                encabezado.FechaUltimaActualizacion = DateTime.Now;
                encabezado.VecesImportado++;
            }

            // 8. Procesar cada línea
            foreach (var linea in lineas)
            {
                if (linea.Concepto == ConceptoPago.Ignorar) continue;

                var rfcKey = linea.RFC.ToUpper().Trim();
                if (!sociosPorRFC.TryGetValue(rfcKey, out var socio))
                {
                    res.SociosNoEncontrados++;
                    log.Add($"[WARN] RFC no es socio de la caja: {rfcKey} — {linea.Nombre}");
                    continue;
                }

                if (linea.Concepto == ConceptoPago.Credito)
                {
                    await AplicarPagoCredito(socio.Id, res.Quincena, res.Anio,
                                             linea.Importe, encabezado.Id, log, res);
                }
                else
                {
                    await AplicarPagoAhorro(socio.Id, res.Quincena, res.Anio,
                                            linea.Importe, linea.Concepto, encabezado.Id, log, res);
                }
            }

            // 9. Actualizar totales del encabezado
            encabezado.TotalLineas = res.TotalLineasArchivo;
            encabezado.CreditosAplicados = res.CreditosAplicados;
            encabezado.AhorrosAplicados = res.AhorrosAplicados;
            encabezado.ImporteCreditos = res.ImporteCreditos;
            encabezado.ImporteAhorros = res.ImporteAhorros;
            await _db.SaveChangesAsync();

            log.Add($"[OK] Créditos aplicados: {res.CreditosAplicados} (${res.ImporteCreditos:N2})");
            log.Add($"[OK] Ahorros aplicados:  {res.AhorrosAplicados} (${res.ImporteAhorros:N2})");
            log.Add($"[OK] Socios no encontrados en caja: {res.SociosNoEncontrados}");
        }
        catch (Exception ex)
        {
            res.Exitoso = false;
            res.Error = ex.Message;
            log.Add($"[ERROR FATAL] {ex.Message}");
            log.Add($"[STACK] {ex.StackTrace?.Split('\n').FirstOrDefault()}");
        }

        return res;
    }

    // ── Selección de pestaña ─────────────────────────────────────────────────

    private static IXLWorksheet? EncontrarPestanaQuincena(XLWorkbook wb, List<string> log)
    {
        // Ignorar pestañas de catálogo o auxiliares
        var ignorar = new[] { "PRESTAMOS TTIB", "HOJA1", "HOJA 1", "RESUMEN", "QNAS 2024", "QNAS 20" };

        foreach (var ws in wb.Worksheets)
        {
            var nombreUp = ws.Name.ToUpper().Trim();
            if (ignorar.Any(i => nombreUp.Contains(i))) continue;
            if (nombreUp.Contains("QNA") || nombreUp.Contains("QUINCENA"))
            {
                log.Add($"[INFO] Pestañas disponibles en el archivo: {string.Join(", ", wb.Worksheets.Select(w => w.Name))}");
                return ws;
            }
        }

        // Si el archivo tiene UNA sola hoja útil
        var unica = wb.Worksheets.FirstOrDefault(ws =>
            !ignorar.Any(i => ws.Name.ToUpper().Contains(i)));
        return unica;
    }

    // ── Detección de formato ─────────────────────────────────────────────────

    private static string DetectarFormato(IXLWorksheet ws)
    {
        // IMSS BIENESTAR: fila 1 tiene "RFC" en A1
        var a1 = ws.Cell(1, 1).GetValue<string>().Trim().ToUpper();
        if (a1 == "RFC") return "IMSS";

        // SSZ: fila 1 tiene "SERVICIOS DE SALUD DE ZACATECAS"
        if (a1.Contains("SERVICIOS DE SALUD") || a1.Contains("ZACATECAS"))
            return "SSZ";

        // TTIB/OPD: fila 4 tiene encabezados con "RFC" en A4
        var a4 = ws.Cell(4, 1).GetValue<string>().Trim().ToUpper();
        if (a4 == "RFC") return "TTIB";

        // Intentar fila 2
        var a2 = ws.Cell(2, 1).GetValue<string>().Trim().ToUpper();
        if (a2 == "RFC") return "IMSS";

        return "SSZ"; // default — cualquier archivo con encabezados de título
    }

    // ── Lectura formato IMSS BIENESTAR ───────────────────────────────────────
    // Columnas: RFC | CURP | APaterno | AMaterno | Nombres | CtoPtda | NombreCto | Importe | Estatus

    private List<LineaPago> LeerFormatoIMSS(IXLWorksheet ws, List<string> log, ResultadoImportacion res)
    {
        var lista = new List<LineaPago>();
        int filaInicio = 2;

        // Si los encabezados están en fila 2 (QNA 02 en adelante)
        var a1 = ws.Cell(1, 1).GetValue<string>().Trim().ToUpper();
        var a2 = ws.Cell(2, 1).GetValue<string>().Trim().ToUpper();
        if (a1 == "RFC") filaInicio = 2;
        else if (a2 == "RFC") filaInicio = 3;

        int lastRow = ws.LastRowUsed()?.RowNumber() ?? filaInicio;

        for (int r = filaInicio; r <= lastRow; r++)
        {
            var rfc = ws.Cell(r, 1).GetValue<string>().Trim().ToUpper();

            // Validar RFC mínimo
            if (string.IsNullOrWhiteSpace(rfc) || rfc.Length < 12 ||
                rfc == "RFC" || rfc.StartsWith("TOTAL") || rfc.StartsWith("ETIQUETA"))
            {
                res.LineasIgnoradas++;
                continue;
            }

            var ap = ws.Cell(r, 3).GetValue<string>().Trim();
            var am = ws.Cell(r, 4).GetValue<string>().Trim();
            var nom = ws.Cell(r, 5).GetValue<string>().Trim();
            var nombre = $"{ap} {am} {nom}".Trim();
            var cto = ws.Cell(r, 7).GetValue<string>().Trim().ToUpper();
            var importe = ObtenerDecimal(ws, r, 8);
            var status = ws.Cell(r, 9).GetValue<string>().Trim().ToUpper();

            if (importe <= 0) { res.LineasIgnoradas++; continue; }

            var concepto = ClasificarConcepto(cto);
            if (concepto == ConceptoPago.Ignorar) { res.LineasIgnoradas++; continue; }

            res.TotalLineasArchivo++;
            lista.Add(new LineaPago
            {
                RFC = rfc,
                Nombre = nombre,
                Importe = importe,
                Concepto = concepto,
                Status = status,
            });
        }

        return lista;
    }

    // ── Lectura formato TTIB ─────────────────────────────────────────────────
    // Columnas: RFC | NOMBRE COMPLETO | Qna XX | Total
    // Solo tiene préstamos (créditos)

    private List<LineaPago> LeerFormatoSSZ_TTIB(IXLWorksheet ws, List<string> log, ResultadoImportacion res)
    {
        var lista = new List<LineaPago>();

        // Buscar fila de encabezado (donde está "RFC")
        int filaEnc = 4;
        for (int r = 1; r <= 8; r++)
        {
            if (ws.Cell(r, 1).GetValue<string>().Trim().ToUpper() == "RFC")
            { filaEnc = r; break; }
        }

        int lastRow = ws.LastRowUsed()?.RowNumber() ?? filaEnc;

        for (int r = filaEnc + 1; r <= lastRow; r++)
        {
            var rfc = ws.Cell(r, 1).GetValue<string>().Trim().ToUpper();

            if (string.IsNullOrWhiteSpace(rfc) || rfc.Length < 12 ||
                rfc.StartsWith("TOTAL") || rfc == "RFC")
            {
                res.LineasIgnoradas++;
                continue;
            }

            var nombre = ws.Cell(r, 2).GetValue<string>().Trim();
            var importe = ObtenerDecimal(ws, r, 3); // columna del monto quincenal

            if (importe <= 0) { res.LineasIgnoradas++; continue; }

            res.TotalLineasArchivo++;
            lista.Add(new LineaPago
            {
                RFC = rfc,
                Nombre = nombre,
                Importe = importe,
                Concepto = ConceptoPago.Credito, // TTIB solo tiene créditos
                Status = "APLICADO",
            });
        }

        return lista;
    }

    // ── Clasificar concepto por NombreCto ────────────────────────────────────

    private static ConceptoPago ClasificarConcepto(string nombreCto)
    {
        if (nombreCto.Contains("PRESTAMO"))
            return ConceptoPago.Credito;

        if (nombreCto.Contains("FONDO DE AHORRO DE SINDICATO") ||
            nombreCto.Contains("FONDO DE AHORRO SINDICATO"))
            return ConceptoPago.AhorroSindicato;

        if (nombreCto.Contains("AHORRO"))
            return ConceptoPago.AhorroOtro;

        // Navideño, Escolar, Personales — no son de la caja de ahorro de créditos
        return ConceptoPago.Ignorar;
    }

    // ── Aplicar pago de crédito ──────────────────────────────────────────────

    private async Task AplicarPagoCredito(
     int socioId, int quincena, int anio, decimal monto,
     int encabezadoId, List<string> log, ResultadoImportacion res)
    {
        // Buscar las líneas de amortización del socio para ESTA quincena/año.
        // Regla: un pago de la quincena N solo puede aplicarse a la quincena N.
        // Nunca se recorre hacia otra quincena (eso causaba aplicar pagos a
        // quincenas no cargadas, sobre todo al reimportar un archivo).
        var detallesQna = await _db.AmortizacionDetalles
            .Include(a => a.Prestamo)
            .Where(a => a.Prestamo.SocioId == socioId
                     && a.Prestamo.Status == StatusPrestamo.Activo
                     && a.Quincena == quincena
                     && a.Anio == anio)
            .OrderBy(a => a.PrestamoId)
            .ToListAsync();

        // Idempotencia en reimportación: si este mismo encabezado ya aplicó a
        // una línea de esta quincena, reusarla (se sobrescribe, no se recorre).
        // Primera importación: tomar la primera línea Pendiente de la quincena
        // (soporta dos créditos del mismo socio en la misma quincena).
        var detalle = detallesQna.FirstOrDefault(a => a.PagoQuincenalId == encabezadoId)
                   ?? detallesQna.FirstOrDefault(a => a.StatusPago == StatusPago.Pendiente);

        if (detalle == null)
        {
            // No hay amortización esperada (Pendiente) para esta quincena.
            // Crédito sin plan de pago correspondiente: se reporta, NO se inventa.
            res.Advertencias.Add($"Crédito sin amortización pendiente para socioId={socioId} en QNA {quincena:D2}/{anio} (${monto:N2}).");
            log.Add($"[WARN] Crédito sin amortización pendiente: socioId={socioId} QNA {quincena:D2}/{anio} (${monto:N2})");
            return;
        }

        detalle.MontoAplicado = monto;
        detalle.PagoQuincenalId = encabezadoId;
        detalle.StatusPago = monto == 0 ? StatusPago.Suspendido
                                : Math.Abs(monto - detalle.Pago) <= 1.0m ? StatusPago.Aplicado
                                : StatusPago.Diferencia;

        res.CreditosAplicados++;
        res.ImporteCreditos += monto;
    }
    // ── Aplicar pago de ahorro ───────────────────────────────────────────────

    private async Task AplicarPagoAhorro(
        int socioId, int quincena, int anio, decimal monto,
        ConceptoPago concepto, int encabezadoId, List<string> log, ResultadoImportacion res)
    {
        // Upsert: si ya existe el ahorro de esta quincena para este socio, actualizar
        var ahorro = await _db.Ahorros
            .FirstOrDefaultAsync(a => a.SocioId == socioId
                                   && a.Quincena == quincena
                                   && a.Anio == anio);
        if (ahorro == null)
        {
            ahorro = new Ahorro
            {
                SocioId = socioId,
                Quincena = quincena,
                Anio = anio,
                MontoRetenido = monto,
                Status = StatusAhorro.Aplicado,
                FechaRegistro = DateTime.Now,
                PagoQuincenalId = encabezadoId,
            };
            _db.Ahorros.Add(ahorro);
        }
        else
        {
            ahorro.MontoRetenido = monto;
            ahorro.Status = StatusAhorro.Aplicado;
            ahorro.PagoQuincenalId = encabezadoId;
        }

        res.AhorrosAplicados++;
        res.ImporteAhorros += monto;
    }

    // ── Historial ────────────────────────────────────────────────────────────

    public async Task<List<PagoQuincenalEncabezado>> ObtenerHistorialAsync()
    {
        return await _db.PagoQuincenalEncabezados
            .OrderByDescending(e => e.Anio)
            .ThenByDescending(e => e.Quincena)
            .ToListAsync();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (int quincena, int anio) ExtraerQuincenaAnio(string nombrePestana, List<string> log)
    {
        // Patrones: "QNA. 01 2025", "QNA.  03  2025", "QNA 09/2025"
        var match = Regex.Match(nombrePestana,
            @"QNA\.?\s*(\d{1,2})\s*[/\-]?\s*(20\d{2})",
            RegexOptions.IgnoreCase);

        if (match.Success
            && int.TryParse(match.Groups[1].Value, out int q)
            && int.TryParse(match.Groups[2].Value, out int a)
            && q >= 1 && q <= 24)
        {
            return (q, a);
        }

        log.Add($"[WARN] No se pudo extraer QNA del nombre «{nombrePestana}» — se usará QNA 1 del año actual.");
        return (1, DateTime.Today.Year);
    }

    private static decimal ObtenerDecimal(IXLWorksheet ws, int row, int col)
    {
        var cell = ws.Cell(row, col);
        if (cell.IsEmpty()) return 0;
        var v = cell.Value;
        if (v.IsNumber) return (decimal)v.GetNumber();
        if (decimal.TryParse(v.ToString()?.Replace(",", ""), out var d)) return d;
        return 0;
    }

    // ── DTO interno ───────────────────────────────────────────────────────────

    private class LineaPago
    {
        public string RFC { get; set; } = "";
        public string Nombre { get; set; } = "";
        public decimal Importe { get; set; }
        public ConceptoPago Concepto { get; set; }
        public string Status { get; set; } = "";
    }
}