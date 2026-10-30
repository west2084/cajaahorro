using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using CajaAhorro.Data;
using CajaAhorro.Models;
using System.Text.RegularExpressions;

namespace CajaAhorro.Services;

public class RetencionService
{
    private readonly AppDbContext _db;

    public RetencionService(AppDbContext db)
    {
        _db = db;
    }

    // ── Resultado ─────────────────────────────────────────────────────────────

    public class ResultadoRetencion
    {
        public int TotalLineasArchivo    { get; set; }
        public int SociosEncontrados     { get; set; }
        
        public int LineasNuevas          { get; set; }
        public int LineasActualizadas    { get; set; }
        public int LineasOmitidas        { get; set; }
        public decimal ImporteTotal      { get; set; }
        public bool EsReimportacion      { get; set; }
        public int VecesImportado        { get; set; }
        public string QuincenaAnio       { get; set; } = "";
        public string Fuente             { get; set; } = "";
        public List<string> Errores      { get; set; } = new();
        public List<string> Log          { get; set; } = new();
        public bool Exitoso              => !Errores.Any();
    }

    // ── Punto de entrada principal ────────────────────────────────────────────

    public async Task<ResultadoRetencion> ImportarAsync(
        Stream archivoExcel,
        string nombreArchivo,
        List<string> log)
    {
        var resultado = new ResultadoRetencion();

        // 1. Leer el Excel en memoria
        using var wb = new XLWorkbook(archivoExcel);
        var ws = wb.Worksheet(1);

        // 2. Extraer metadatos del encabezado
        var titulo = ws.Cell(1, 1).GetValue<string>().Trim();  // fila 1 = institución
        var tituloCompleto = "";
        for (int r = 1; r <= 5; r++)
        {
            var v = ws.Cell(r, 1).GetValue<string>().Trim();
            if (v.Length > tituloCompleto.Length) tituloCompleto = v;
        }

        log.Add($"[INFO] Título: {tituloCompleto}");

        var (quincena, anio) = ExtraerQuincenaAnio(tituloCompleto, log);
        var fuente = DeterminarFuente(titulo, tituloCompleto);

        resultado.QuincenaAnio = $"QNA {quincena:D2}/{anio}";
        resultado.Fuente = fuente == FuenteRetencion.Estatal ? "Estatal (SSZ)" : "Federal (OPD)";

        log.Add($"[INFO] Quincena detectada: {quincena}/{anio} — Fuente: {resultado.Fuente}");

        // 3. Encontrar fila de encabezados (buscar fila con "RFC")
        int filaEncabezado = 6;
        for (int r = 1; r <= 10; r++)
        {
            if (ws.Cell(r, 1).GetValue<string>().Trim().Equals("RFC", StringComparison.OrdinalIgnoreCase))
            {
                filaEncabezado = r;
                break;
            }
        }
        log.Add($"[INFO] Encabezados en fila {filaEncabezado}, datos desde fila {filaEncabezado + 1}");

        // 4. Leer todas las líneas válidas del Excel
        var lineasExcel = LeerLineasExcel(ws, filaEncabezado, log, resultado);

        log.Add($"[INFO] Líneas válidas leídas: {lineasExcel.Count} — Importe total: {resultado.ImporteTotal:C}");

        // 5. Cargar socios para cruzar por RFC
        var sociosPorRFC = await _db.Socios
            .Include(s => s.Instituciones)
            .ToDictionaryAsync(s => s.RFC.ToUpper().Trim(), StringComparer.OrdinalIgnoreCase);

        // 6. UPSERT del encabezado (clave: Quincena + Anio + Fuente)
        var encabezado = await _db.RetencionEncabezados
            .Include(e => e.Lineas)
            .FirstOrDefaultAsync(e => e.Quincena == quincena
                                   && e.Anio == anio
                                   && e.Fuente == fuente);

        bool esReimportacion = encabezado != null;
        resultado.EsReimportacion = esReimportacion;

        if (encabezado == null)
        {
            encabezado = new RetencionEncabezado
            {
                Quincena       = quincena,
                Anio           = anio,
                Fuente         = fuente,
                TituloOriginal = tituloCompleto,
                NombreArchivo  = nombreArchivo,
                FechaCarga     = DateTime.Now,
                VecesImportado = 1,
            };
            _db.RetencionEncabezados.Add(encabezado);
            await _db.SaveChangesAsync();
            log.Add($"[NUEVO] Encabezado creado Id={encabezado.Id}");
        }
        else
        {
            encabezado.VecesImportado++;
            encabezado.NombreArchivo            = nombreArchivo;
            encabezado.TituloOriginal           = tituloCompleto;
            encabezado.FechaUltimaActualizacion = DateTime.Now;
            log.Add($"[UPDATE] Reimportación #{encabezado.VecesImportado} — encabezado Id={encabezado.Id}");
        }

        resultado.VecesImportado = encabezado.VecesImportado;

        // 7. UPSERT de líneas (clave: RFC dentro del encabezado)
        var lineasExistentes = encabezado.Lineas
            .ToDictionary(l => l.RFC.ToUpper().Trim(), StringComparer.OrdinalIgnoreCase);

        int sociosEncontrados = 0;
        int prestamosActualizados = 0;

        foreach (var lineaExcel in lineasExcel)
        {
            var rfcKey = lineaExcel.RFC.ToUpper().Trim();

            // Cruzar con socios de la caja
            sociosPorRFC.TryGetValue(rfcKey, out var socio);
            bool esSocio = socio != null;
            if (esSocio) sociosEncontrados++;

            // Detectar migración: el socio existe pero su institución activa
            // en esta quincena es diferente a la fuente del archivo actual.
            bool esMigracion = false;
            if (esSocio && socio!.Instituciones.Any())
            {
                var instEnQna    = socio.InstitucionEn(quincena, anio);
                var instArchivo  = fuente == FuenteRetencion.Federal
                                   ? InstitucionTipo.Federal
                                   : InstitucionTipo.Estatal;
                esMigracion = instEnQna != instArchivo;
                if (esMigracion)
                    log.Add($"[MIGRACION] {rfcKey} aparece en {resultado.Fuente} " +
                            $"pero su institución activa en QNA {quincena:D2}/{anio} es {instEnQna}");
            }

            if (lineasExistentes.TryGetValue(rfcKey, out var linea))
            {
                linea.NombreEmpleado    = lineaExcel.NombreEmpleado;
                linea.Importe           = lineaExcel.Importe;
                linea.UnidadResponsable = lineaExcel.UnidadResponsable;
                linea.Concepto          = lineaExcel.Concepto;
                linea.EsSocio           = esSocio;
                linea.SocioId           = socio?.Id;
                linea.EsMigracion       = esMigracion;
                resultado.LineasActualizadas++;
            }
            else
            {
                var nueva = new RetencionLinea
                {
                    RetencionEncabezadoId = encabezado.Id,
                    RFC                   = lineaExcel.RFC,
                    NombreEmpleado        = lineaExcel.NombreEmpleado,
                    Importe               = lineaExcel.Importe,
                    UnidadResponsable     = lineaExcel.UnidadResponsable,
                    Concepto              = lineaExcel.Concepto,
                    EsSocio               = esSocio,
                    SocioId               = socio?.Id,
                    EsMigracion           = esMigracion,
                };
                _db.RetencionLineas.Add(nueva);
                resultado.LineasNuevas++;
            }

            // Actualizar AmortizacionDetalle si el socio tiene préstamo activo
            if (esSocio)
            {
                var actualizado = await ActualizarAmortizacion(socio!.Id, quincena, anio, lineaExcel.Importe);
                if (actualizado) prestamosActualizados++;
            }
        }

        // 8. Eliminar líneas que ya no existen en el archivo (correcciones de RFC)
        var rfcsDelExcel = new HashSet<string>(
            lineasExcel.Select(l => l.RFC.ToUpper().Trim()),
            StringComparer.OrdinalIgnoreCase);

        foreach (var linea in lineasExistentes.Values)
        {
            if (!rfcsDelExcel.Contains(linea.RFC.ToUpper().Trim()))
            {
                _db.RetencionLineas.Remove(linea);
                log.Add($"[BORRADO] RFC {linea.RFC} ya no aparece en el nuevo archivo");
            }
        }

        // 9. Actualizar totales del encabezado
        encabezado.TotalRegistros       = lineasExcel.Count;
        encabezado.ImporteTotal         = resultado.ImporteTotal;
        encabezado.SociosEncontrados    = sociosEncontrados;

        await _db.SaveChangesAsync();

        resultado.TotalLineasArchivo    = lineasExcel.Count;
        resultado.SociosEncontrados     = sociosEncontrados;

        log.Add($"[OK] Nuevas:{resultado.LineasNuevas} Actualizadas:{resultado.LineasActualizadas} Omitidas:{resultado.LineasOmitidas}");
        log.Add($"[OK] Socios de la caja encontrados: {sociosEncontrados} — Amortizaciones actualizadas: {prestamosActualizados}");

        return resultado;
    }

    // ── Leer líneas del Excel ─────────────────────────────────────────────────

    private List<RetencionLinea> LeerLineasExcel(
        IXLWorksheet ws,
        int filaEncabezado,
        List<string> log,
        ResultadoRetencion resultado)
    {
        var lista = new List<RetencionLinea>();
        int lastRow = ws.LastRowUsed()?.RowNumber() ?? filaEncabezado;
        decimal importeTotal = 0;

        for (int r = filaEncabezado + 1; r <= lastRow; r++)
        {
            var rfcRaw = ws.Cell(r, 1).GetValue<string>().Trim();

            // Saltar filas sin RFC válido (totales, secciones, cancelados, etc.)
            if (string.IsNullOrWhiteSpace(rfcRaw)
                || rfcRaw.Length < 12
                || rfcRaw.StartsWith("<")
                || rfcRaw.Equals("RFC", StringComparison.OrdinalIgnoreCase)
                || rfcRaw.StartsWith("TOTAL", StringComparison.OrdinalIgnoreCase)
                || rfcRaw.StartsWith("RESUMEN", StringComparison.OrdinalIgnoreCase))
            {
                resultado.LineasOmitidas++;
                continue;
            }

            var nombre  = ws.Cell(r, 2).GetValue<string>().Trim();
            decimal imp = ObtenerDecimal(ws, r, 3);
            var unidad  = ws.Cell(r, 7).GetValue<string>().Trim();

            // Omitir líneas de "CHEQUES CANCELADOS" (importe negativo o sección diferente)
            if (imp < 0)
            {
                resultado.LineasOmitidas++;
                continue;
            }

            // Importe 0 puede ser dato inválido
            if (imp == 0 && string.IsNullOrEmpty(nombre))
            {
                resultado.LineasOmitidas++;
                continue;
            }

            importeTotal += imp;

            lista.Add(new RetencionLinea
            {
                RFC               = rfcRaw.ToUpper(),
                NombreEmpleado    = nombre.ToUpper(),
                Importe           = imp,
                UnidadResponsable = unidad,
                Concepto          = "PRESTAMO",
            });
        }

        resultado.ImporteTotal = importeTotal;
        return lista;
    }

    // ── Cruzar con amortización ───────────────────────────────────────────────

    private async Task<bool> ActualizarAmortizacion(
        int socioId, int quincena, int anio, decimal montoAplicado)
    {
        // Buscar la línea de amortización que corresponde a esta quincena
        var detalle = await _db.AmortizacionDetalles
            .Include(a => a.Prestamo)
            .Where(a => a.Prestamo.SocioId == socioId
                     && a.Prestamo.Status == StatusPrestamo.Activo
                     && a.Quincena == quincena
                     && a.Anio == anio)
            .OrderBy(a => a.PrestamoId)
            .FirstOrDefaultAsync();

        if (detalle == null) return false;

        detalle.MontoAplicado = montoAplicado;

        // Determinar status del pago
        if (montoAplicado == 0)
            detalle.StatusPago = StatusPago.Suspendido;
        else if (Math.Abs(montoAplicado - detalle.Pago) > 1m)
            detalle.StatusPago = StatusPago.Diferencia;
        else
            detalle.StatusPago = StatusPago.Aplicado;

        return true;
    }

    // ── Extraer QNA/Año del título ────────────────────────────────────────────

    private static (int quincena, int anio) ExtraerQuincenaAnio(string titulo, List<string> log)
    {
        // Buscar patrón "QNA. 09/2026" o "QNA 09/2026" o "QNA. 9/2026"
        var match = Regex.Match(titulo,
            @"QNA\.?\s*(\d{1,2})[/\-](\d{4})",
            RegexOptions.IgnoreCase);

        if (match.Success
            && int.TryParse(match.Groups[1].Value, out int q)
            && int.TryParse(match.Groups[2].Value, out int a)
            && q >= 1 && q <= 24)
        {
            log.Add($"[INFO] QNA extraída del título: {q}/{a}");
            return (q, a);
        }

        // Fallback: buscar 4 dígitos de año y 1-2 de quincena por separado
        var matchAnio = Regex.Match(titulo, @"(20\d{2})");
        int anio = matchAnio.Success ? int.Parse(matchAnio.Groups[1].Value) : DateTime.Today.Year;

        log.Add($"[WARN] No se pudo extraer QNA del título — usando 1/{anio}. Revisa el archivo.");
        return (1, anio);
    }

    private static FuenteRetencion DeterminarFuente(string lineaUno, string titulo)
    {
        var t = (lineaUno + " " + titulo).ToUpper();
        if (t.Contains("OPD") || t.Contains("FEDERAL") || t.Contains("IMSS"))
            return FuenteRetencion.Federal;
        return FuenteRetencion.Estatal;
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
}
