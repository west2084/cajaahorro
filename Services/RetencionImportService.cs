using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using CajaAhorro.Data;
using CajaAhorro.Models;
using CajaAhorro.Helpers;
using System.Text.RegularExpressions;

namespace CajaAhorro.Services;

/// <summary>
/// Servicio unificado para importar archivos de retenciones quincenales.
/// Soporta tres formatos:
///   - SERVICIOS DE SALUD (SS): encabezado en fila 6, columnas RFC/NOMBRE/IMPORTE/VIGENCIA/TIPO NOMINA/...
///   - IMSS BIENESTAR TTIB: encabezado en fila 4, columnas RFC/NOMBRE COMPLETO/Qna XX/Total
///   - IMSS BIENESTAR PRÉSTAMOS: encabezado en fila 5, columnas
///     RFC/CURP/APaterno/AMaterno/Nombres/NombreCto/Código Tabular/Suma de Importe.
///     Trae CURP (se usa para completar el dato en Socios si falta) y suele
///     venir en una sola hoja (p. ej. "Hoja1") sin "QNA" en el nombre de la
///     pestaña — la quincena/año se saca del TÍTULO (fila 1), no del nombre
///     de la pestaña.
/// El formato se detecta automáticamente por la estructura de la hoja.
/// </summary>
public class RetencionImportService
{
    private readonly AppDbContext _db;

    public RetencionImportService(AppDbContext db) => _db = db;

    // ── Institución origen del archivo ───────────────────────────────────────

    public enum Institucion { ServiciosSalud, ImssB }

    // ── DTO de resultado ─────────────────────────────────────────────────────

    public class ResultadoImportacion
    {
        public int Quincena { get; set; }
        public int Anio { get; set; }
        public Institucion Institucion { get; set; }
        public string PestanaLeida { get; set; } = "";
        public string FormatoDetectado { get; set; } = "";

        public int TotalLineasArchivo { get; set; }
        public int LineasGuardadas { get; set; }   // líneas persistidas en RetencionLinea (todo el universo)
        public int CreditosAplicados { get; set; }
        public int SociosNoEncontrados { get; set; }
        public int SociosProvisionales { get; set; }   // socios dados de alta automáticamente
        public int LineasIgnoradas { get; set; }
        public int SinAmortizacion { get; set; }

        public decimal ImporteTotal { get; set; }
        public bool EsReimportacion { get; set; }
        public bool Exitoso { get; set; } = true;
        public string? Error { get; set; }

        public List<string> Log { get; set; } = new();
        public List<string> Advertencias { get; set; } = new();
    }

    // ── Línea normalizada (independiente del formato) ────────────────────────

    private record LineaRetencion(
        string RFC,
        string Nombre,
        decimal Importe,
        string TipoNomina,   // "O", "REESTRUCTURA", etc. — solo SS lo trae
        string? Vigencia,     // "202511" — solo SS lo trae
        string UnidadResponsable = "",   // SSZ col 7; en "IMSS Bienestar Préstamos" se reutiliza para el Código Tabular
        string? CURP = null   // solo el formato "IMSS Bienestar Préstamos" lo trae
    );

    // ── Punto de entrada ─────────────────────────────────────────────────────

    public async Task<ResultadoImportacion> ImportarAsync(
        Stream archivoExcel,
        string nombreArchivo,
        int? quincenaOverride = null,
        int? anioOverride = null)
    {
        var res = new ResultadoImportacion();

        try
        {
            using var wb = new XLWorkbook(archivoExcel);

            // 1. Seleccionar la hoja de quincena correcta
            var hoja = SeleccionarHoja(wb, res.Log);
            if (hoja is null)
            {
                res.Exitoso = false;
                res.Error = "No se encontró una pestaña de quincena válida en el archivo.";
                return res;
            }

            res.PestanaLeida = hoja.Name;
            res.Log.Add($"[INFO] Pestaña seleccionada: «{hoja.Name}»");

            // 2. Detectar formato
            var (formato, inst) = DetectarFormato(hoja);
            res.FormatoDetectado = formato;
            res.Institucion = inst;
            res.Log.Add($"[INFO] Formato: {formato}");

            // 3. Extraer quincena/año.
            //    El formato "IMSS BIENESTAR PRÉSTAMOS" suele venir en una sola
            //    hoja sin info de quincena en el NOMBRE de la pestaña (p. ej.
            //    "Hoja1") — el dato está en el TÍTULO (fila 1) en su lugar.
            var (qna, anio) = formato == "IMSS BIENESTAR PRESTAMOS"
                ? ExtraerQuincenaAnio(hoja.Cell(1, 1).GetValue<string>(), res.Log)
                : ExtraerQuincenaAnio(hoja.Name, res.Log);
            res.Quincena = quincenaOverride ?? qna;
            res.Anio = anioOverride ?? anio;
            res.Log.Add($"[INFO] Quincena: {res.Quincena:D2}/{res.Anio}");

            // 4. Leer líneas según formato
            List<LineaRetencion> lineas = formato switch
            {
                "SERVICIOS DE SALUD" => LeerServiciosSalud(hoja, res),
                "IMSS BIENESTAR PRESTAMOS" => LeerImssB_Prestamos(hoja, res),
                _ => LeerImssB_TTIB(hoja, res)
            };

            res.Log.Add($"[INFO] Líneas válidas leídas: {lineas.Count}");

            // 4b. Consolidar RFC duplicados DENTRO del mismo archivo.
            //     Nómina puede listar a un mismo RFC en varios renglones
            //     (dos préstamos, o préstamo + otro concepto). El universo
            //     guarda UN renglón por RFC (índice único Encabezado+RFC).
            //     - Si los renglones duplicados tienen IMPORTES DISTINTOS,
            //       son conceptos distintos de verdad → se SUMAN.
            //     - Si tienen el importe EXACTAMENTE IGUAL, lo más probable
            //       es un renglón repetido por error del archivo origen (visto
            //       en archivos reales de IMSS Bienestar) → se toma UNA sola
            //       vez, nunca se duplica el pago aplicado al crédito.
            var antesDedup = lineas.Count;
            int consolidadosSumados = 0, consolidadosRepetidos = 0;
            lineas = lineas
                .GroupBy(l => l.RFC.ToUpper().Trim())
                .Select(g =>
                {
                    if (g.Count() == 1) return g.First();

                    bool mismoImporte = g.Select(x => Math.Round(x.Importe, 2)).Distinct().Count() == 1;
                    if (mismoImporte)
                    {
                        consolidadosRepetidos++;
                        res.Log.Add($"[AVISO] RFC {g.Key} aparece {g.Count()} veces con el MISMO importe " +
                                    $"(${g.First().Importe:N2}) — se toma una sola vez (probable renglón repetido, revisa el archivo si no era la intención).");
                        return g.First();
                    }

                    consolidadosSumados++;
                    return g.First() with { Importe = g.Sum(x => x.Importe) };
                })
                .ToList();
            int consolidados = antesDedup - lineas.Count;
            if (consolidados > 0)
                res.Log.Add($"[INFO] RFC duplicados consolidados: {consolidadosSumados} sumados (conceptos distintos), " +
                            $"{consolidadosRepetidos} tomados una sola vez (mismo importe repetido).");

            // 5. Verificar reimportación
            var yaExiste = await _db.PagoQuincenalEncabezados
                .AnyAsync(e => e.Quincena == res.Quincena
                            && e.Anio == res.Anio
                            && e.NombreArchivo == nombreArchivo);
            res.EsReimportacion = yaExiste;
            if (yaExiste)
                res.Log.Add("[WARN] Archivo ya importado — se ACTUALIZARÁN los registros existentes.");

            // 6. Upsert encabezado
            var enc = await _db.PagoQuincenalEncabezados
                .FirstOrDefaultAsync(e => e.Quincena == res.Quincena
                                       && e.Anio == res.Anio
                                       && e.NombreArchivo == nombreArchivo);
            if (enc is null)
            {
                enc = new PagoQuincenalEncabezado
                {
                    Quincena = res.Quincena,
                    Anio = res.Anio,
                    NombreArchivo = nombreArchivo,
                    Formato = formato,
                    FechaCarga = DateTime.Now,
                };
                _db.PagoQuincenalEncabezados.Add(enc);
                await _db.SaveChangesAsync();
            }
            else
            {
                enc.FechaUltimaActualizacion = DateTime.Now;
                enc.VecesImportado++;
            }

            // 7. Cargar socios por RFC (una sola consulta)
            var sociosPorRfc = await _db.Socios
                .ToDictionaryAsync(s => s.RFC.ToUpper().Trim(), StringComparer.OrdinalIgnoreCase);

            // 7b. Alta de socios PROVISIONALES para los RFC que no existen.
            //     No se pierde ninguna línea: si el RFC trae dinero pero no está
            //     capturado, se da de alta con RFC + nombre, marcado Provisional.
            var provisionalesNuevos = new List<Socio>();
            var rfcsProcesados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var linea in lineas)
            {
                var rfc = linea.RFC.ToUpper().Trim();
                if (sociosPorRfc.ContainsKey(rfc)) continue;
                if (!rfcsProcesados.Add(rfc)) continue;

                var prov = new Socio
                {
                    RFC = rfc,
                    Nombre = string.IsNullOrWhiteSpace(linea.Nombre) ? "(SIN NOMBRE)" : linea.Nombre,
                    CURP = string.IsNullOrWhiteSpace(linea.CURP) ? null : linea.CURP.Trim().ToUpper(),
                    EstadoCaptura = EstadoCaptura.Provisional,
                    Activo = true,
                    FechaRegistro = DateTime.Now,
                };
                _db.Socios.Add(prov);
                provisionalesNuevos.Add(prov);
            }
            if (provisionalesNuevos.Count > 0)
            {
                await _db.SaveChangesAsync();   // asigna Id antes de enlazar líneas
                foreach (var p in provisionalesNuevos)
                    sociosPorRfc[p.RFC.ToUpper().Trim()] = p;
                res.SociosProvisionales = provisionalesNuevos.Count;
                res.Log.Add($"[NUEVO] Socios provisionales dados de alta: {provisionalesNuevos.Count}");
            }

            // 7c. Encabezado del UNIVERSO de retenciones (todas las líneas, sea socio o no).
            //     Clave: Quincena + Anio + Fuente.
            var fuente = inst == Institucion.ImssB ? FuenteRetencion.Federal : FuenteRetencion.Estatal;
            var retEnc = await _db.RetencionEncabezados
                .Include(e => e.Lineas)
                .FirstOrDefaultAsync(e => e.Quincena == res.Quincena
                                       && e.Anio == res.Anio
                                       && e.Fuente == fuente);
            if (retEnc is null)
            {
                retEnc = new RetencionEncabezado
                {
                    Quincena = res.Quincena,
                    Anio = res.Anio,
                    Fuente = fuente,
                    TituloOriginal = formato,
                    NombreArchivo = nombreArchivo,
                    FechaCarga = DateTime.Now,
                    VecesImportado = 1,
                };
                _db.RetencionEncabezados.Add(retEnc);
                await _db.SaveChangesAsync();
            }
            else
            {
                retEnc.VecesImportado++;
                retEnc.NombreArchivo = nombreArchivo;
                retEnc.FechaUltimaActualizacion = DateTime.Now;
            }
            var lineasExistentes = retEnc.Lineas
                .ToDictionary(l => l.RFC.ToUpper().Trim(), StringComparer.OrdinalIgnoreCase);

            // 8. Procesar cada línea: guardar SIEMPRE el renglón + aplicar al crédito si procede.
            decimal importeUniverso = 0m;
            foreach (var linea in lineas)
            {
                var rfc = linea.RFC.ToUpper().Trim();
                sociosPorRfc.TryGetValue(rfc, out var socio);   // siempre existe tras la pasada 7b
                bool esCompleto = socio is not null && socio.EstadoCaptura == EstadoCaptura.Completo;

                // Completar CURP si el socio no la tenía capturada y este
                // archivo sí la trae (formato "IMSS Bienestar Préstamos").
                if (socio is not null && string.IsNullOrWhiteSpace(socio.CURP)
                    && !string.IsNullOrWhiteSpace(linea.CURP))
                {
                    socio.CURP = linea.CURP.Trim().ToUpper();
                }

                // 8a. Persistir el renglón en el universo (upsert por RFC dentro del encabezado)
                RetencionLinea lineaEntidad;
                if (lineasExistentes.TryGetValue(rfc, out var li))
                {
                    li.NombreEmpleado = Trunc(linea.Nombre, 200);
                    li.Importe = linea.Importe;
                    li.UnidadResponsable = Trunc(linea.UnidadResponsable, 20);
                    li.Concepto = Trunc(linea.TipoNomina, 100);
                    li.EsSocio = esCompleto;
                    li.SocioId = socio?.Id;
                    lineaEntidad = li;
                }
                else
                {
                    lineaEntidad = new RetencionLinea
                    {
                        RetencionEncabezadoId = retEnc.Id,
                        RFC = rfc,
                        NombreEmpleado = Trunc(linea.Nombre, 200),
                        Importe = linea.Importe,
                        UnidadResponsable = Trunc(linea.UnidadResponsable, 20),
                        Concepto = Trunc(linea.TipoNomina, 100),
                        EsSocio = esCompleto,
                        SocioId = socio?.Id,
                    };
                    _db.RetencionLineas.Add(lineaEntidad);
                }
                importeUniverso += linea.Importe;
                res.LineasGuardadas++;

                // 8b. Aplicar al crédito solo si el socio ya está completo
                //     (los provisionales aún no tienen préstamo registrado).
                bool aplicado = false;
                if (socio is not null && esCompleto)
                    aplicado = await AplicarPago(socio.Id, rfc, linea, res.Quincena, res.Anio, enc.Id, res);

                // Marca la línea: si no se aplicó, es un pago SIN crédito asociado
                // (lo que el auxiliar debe investigar y capturar).
                lineaEntidad.AplicadoACredito = aplicado;
            }

            // 9. Totales — encabezado de pagos (compatibilidad con la página)
            enc.TotalLineas = res.TotalLineasArchivo;
            enc.CreditosAplicados = res.CreditosAplicados;
            enc.ImporteCreditos = res.ImporteTotal;

            // 9b. Totales — encabezado del universo de retenciones
            retEnc.TotalRegistros = res.TotalLineasArchivo;
            retEnc.ImporteTotal = importeUniverso;
            retEnc.SociosEncontrados = res.LineasGuardadas - res.SociosProvisionales;
            retEnc.SociosNuevos = res.SociosProvisionales;

            await _db.SaveChangesAsync();

            res.Log.Add($"[OK] Líneas guardadas (universo): {res.LineasGuardadas}  (${importeUniverso:N2})");
            res.Log.Add($"[OK] Provisionales creados: {res.SociosProvisionales}");
            res.Log.Add($"[OK] Créditos aplicados: {res.CreditosAplicados}  (${res.ImporteTotal:N2})");
            if (res.SinAmortizacion > 0)
                res.Log.Add($"[WARN] Sin amortización pendiente: {res.SinAmortizacion}");
        }
        catch (Exception ex)
        {
            res.Exitoso = false;
            // El mensaje útil casi siempre está en la(s) InnerException
            // (DbUpdateException envuelve el error real de SQL Server).
            var inner = ex;
            while (inner.InnerException is not null) inner = inner.InnerException;
            res.Error = inner.Message;
            res.Log.Add($"[ERROR FATAL] {ex.Message}");
            if (!ReferenceEquals(inner, ex))
                res.Log.Add($"[CAUSA REAL] {inner.Message}");
        }

        return res;
    }

    // ── Selección de hoja ────────────────────────────────────────────────────

    private static IXLWorksheet? SeleccionarHoja(XLWorkbook wb, List<string> log)
    {
        // Hojas a ignorar (catálogos, resúmenes)
        var ignorar = new[] { "PRESTAMOS TTIB", "HOJA1", "HOJA 1", "RESUMEN", "QNAS 20" };

        log.Add($"[INFO] Hojas en archivo: {string.Join(", ", wb.Worksheets.Select(w => w.Name))}");

        // Priorizar hojas con "QNA" en el nombre
        var hoja = wb.Worksheets.FirstOrDefault(ws =>
        {
            var n = ws.Name.ToUpper().Trim();
            return !ignorar.Any(i => n.Contains(i))
                && (n.Contains("QNA") || n.Contains("QUINCENA"));
        });

        hoja ??= wb.Worksheets.FirstOrDefault(ws =>
            !ignorar.Any(i => ws.Name.ToUpper().Contains(i)));

        // Último recurso: si TODAS las hojas coinciden con la lista de
        // "ignorar" (p. ej. un archivo de una sola hoja llamada "Hoja1" que
        // SÍ trae los datos reales — visto en el formato "IMSS Bienestar
        // Préstamos"), no dejar la carga sin hoja: usar la primera que haya.
        hoja ??= wb.Worksheets.FirstOrDefault();

        return hoja;
    }

    // ── Detección de formato ─────────────────────────────────────────────────

    private static (string formato, Institucion inst) DetectarFormato(IXLWorksheet ws)
    {
        var f1c1 = ws.Cell(1, 1).GetValue<string>().ToUpper();

        // IMSS Bienestar (préstamos): título como
        // 'RETENCIONES POR CONCEPTO DE "PRESTAMOS - AA39" QNA 01 2026 SECC 39 ZACATECAS'.
        // Se revisa ANTES que "ZACATECAS"/"SERVICIOS DE SALUD" porque ese
        // mismo título también contiene "ZACATECAS" y se confundiría con el
        // formato de Servicios de Salud si se revisara después.
        if (f1c1.Contains("RETENCIONES POR CONCEPTO") || f1c1.Contains("PRESTAMOS - AA39"))
            return ("IMSS BIENESTAR PRESTAMOS", Institucion.ImssB);

        // Servicios de Salud: fila 1 dice "Servicios de Salud de Zacatecas"
        //                     encabezados en fila 6: RFC | NOMBRE | IMPORTE | VIGENCIA | ...
        if (f1c1.Contains("SERVICIOS DE SALUD") || f1c1.Contains("ZACATECAS"))
            return ("SERVICIOS DE SALUD", Institucion.ServiciosSalud);

        // IMSS Bienestar TTIB: fila 1 dice "PAGOS ADICIONALES SNTSA..."
        //                      encabezados en fila 4: RFC | NOMBRE COMPLETO | Qna XX | Total
        if (f1c1.Contains("PAGOS ADICIONALES") || f1c1.Contains("SNTSA") || f1c1.Contains("CAJA DE AHORRO"))
            return ("IMSS BIENESTAR TTIB", Institucion.ImssB);

        // Fallback: revisar fila 4 (TTIB)
        var f4c1 = ws.Cell(4, 1).GetValue<string>().ToUpper().Trim();
        if (f4c1 == "RFC")
            return ("IMSS BIENESTAR TTIB", Institucion.ImssB);

        // Fallback: revisar fila 5 con RFC + CURP (IMSS Bienestar Préstamos)
        var f5c1 = ws.Cell(5, 1).GetValue<string>().ToUpper().Trim();
        var f5c2 = ws.Cell(5, 2).GetValue<string>().ToUpper().Trim();
        if (f5c1 == "RFC" && f5c2 == "CURP")
            return ("IMSS BIENESTAR PRESTAMOS", Institucion.ImssB);

        // Fallback: revisar fila 6 (Servicios de Salud)
        var f6c1 = ws.Cell(6, 1).GetValue<string>().ToUpper().Trim();
        if (f6c1 == "RFC")
            return ("SERVICIOS DE SALUD", Institucion.ServiciosSalud);

        return ("IMSS BIENESTAR TTIB", Institucion.ImssB);
    }

    // ── Lector: Servicios de Salud ───────────────────────────────────────────
    // Estructura: filas 1-4 = encabezado institucional, fila 5 = vacía,
    //             fila 6 = cabecera (RFC|NOMBRE|IMPORTE|VIGENCIA|TIPO NOMINA|TIPO DE EMP|UNIDAD RESPONS.)
    //             fila 7 en adelante = datos

    private List<LineaRetencion> LeerServiciosSalud(IXLWorksheet ws, ResultadoImportacion res)
    {
        var lista = new List<LineaRetencion>();

        // Buscar fila de encabezado (donde la col A dice "RFC")
        int filaEnc = 6;
        for (int r = 1; r <= 10; r++)
        {
            if (ws.Cell(r, 1).GetValue<string>().Trim().ToUpper() == "RFC")
            { filaEnc = r; break; }
        }

        int lastRow = ws.LastRowUsed()?.RowNumber() ?? filaEnc;

        for (int r = filaEnc + 1; r <= lastRow; r++)
        {
            var rfc = ws.Cell(r, 1).GetValue<string>().Trim().ToUpper();

            // Saltar filas vacías, totales y repetición de encabezado
            if (string.IsNullOrWhiteSpace(rfc) || rfc.Length < 12
                || rfc == "RFC" || rfc.StartsWith("TOTAL"))
            {
                res.LineasIgnoradas++;
                continue;
            }

            var nombre = TextoNomina.LimpiarNombre(ws.Cell(r, 2).GetValue<string>());
            var importe = ObtenerDecimal(ws, r, 3);
            var vigencia = ws.Cell(r, 4).GetValue<string>().Trim();
            var tipoNomina = ws.Cell(r, 5).GetValue<string>().Trim();
            var unidad = ws.Cell(r, 7).GetValue<string>().Trim();

            if (importe <= 0) { res.LineasIgnoradas++; continue; }

            res.TotalLineasArchivo++;
            lista.Add(new LineaRetencion(rfc, nombre, importe, tipoNomina, vigencia, unidad));
        }

        return lista;
    }

    // ── Lector: IMSS Bienestar TTIB ─────────────────────────────────────────
    // Estructura: fila 1 = título, filas 2-3 = vacías,
    //             fila 4 = cabecera (RFC | NOMBRE COMPLETO | Qna XX | Total | [REESTRUCTURA fecha]?)
    //             fila 5 en adelante = datos

    private List<LineaRetencion> LeerImssB_TTIB(IXLWorksheet ws, ResultadoImportacion res)
    {
        var lista = new List<LineaRetencion>();

        // Buscar fila de encabezado
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

            if (string.IsNullOrWhiteSpace(rfc) || rfc.Length < 12
                || rfc == "RFC" || rfc.StartsWith("TOTAL"))
            {
                res.LineasIgnoradas++;
                continue;
            }

            var nombre = TextoNomina.LimpiarNombre(ws.Cell(r, 2).GetValue<string>());
            var importe = ObtenerDecimal(ws, r, 3); // columna "Qna XX" = col 3

            if (importe <= 0) { res.LineasIgnoradas++; continue; }

            // Columna 5 puede traer nota de reestructura (ej. "REESTRUCTURA 24/04/2026")
            var nota = ws.Cell(r, 5).GetValue<string>().Trim().ToUpper();
            var tipoNomina = nota.Contains("REESTRUCTURA") ? "REESTRUCTURA" : "O";

            res.TotalLineasArchivo++;
            lista.Add(new LineaRetencion(rfc, nombre, importe, tipoNomina, null));
        }

        return lista;
    }

    // ── Lector: IMSS Bienestar Préstamos ─────────────────────────────────────
    // Estructura: fila 1 (celda combinada) = título con "QNA XX AAAA",
    //             filas 2-4 = vacías,
    //             fila 5 = cabecera (RFC | CURP | APaterno | Amaterno | Nombres
    //                                 | NombreCto | Código Tabular | Suma de Importe)
    //             fila 6 en adelante = datos.
    // A diferencia de los otros dos formatos, SÍ trae CURP — se usa para
    // completar el dato en Socios si aún no la tiene capturada.

    private List<LineaRetencion> LeerImssB_Prestamos(IXLWorksheet ws, ResultadoImportacion res)
    {
        var lista = new List<LineaRetencion>();

        // Buscar fila de encabezado: RFC en la col 1 Y CURP en la col 2
        // (así no se confunde con la cabecera de los otros dos formatos).
        int filaEnc = 5;
        for (int r = 1; r <= 10; r++)
        {
            if (ws.Cell(r, 1).GetValue<string>().Trim().ToUpper() == "RFC"
                && ws.Cell(r, 2).GetValue<string>().Trim().ToUpper() == "CURP")
            { filaEnc = r; break; }
        }

        int lastRow = ws.LastRowUsed()?.RowNumber() ?? filaEnc;

        for (int r = filaEnc + 1; r <= lastRow; r++)
        {
            var rfc = ws.Cell(r, 1).GetValue<string>().Trim().ToUpper();

            if (string.IsNullOrWhiteSpace(rfc) || rfc.Length < 12
                || rfc == "RFC" || rfc.StartsWith("TOTAL"))
            {
                res.LineasIgnoradas++;
                continue;
            }

            var curp = ws.Cell(r, 2).GetValue<string>().Trim().ToUpper();
            var aPaterno = ws.Cell(r, 3).GetValue<string>().Trim();
            var aMaterno = ws.Cell(r, 4).GetValue<string>().Trim();
            var nombres = ws.Cell(r, 5).GetValue<string>().Trim();
            var nombre = TextoNomina.LimpiarNombre($"{aPaterno} {aMaterno} {nombres}".Trim());

            // "Código Tabular" (col 7) no es una unidad responsable de verdad,
            // pero se guarda ahí (campo libre, no usado para nada crítico) por
            // si sirve de referencia al conciliar con IMSS Bienestar.
            var codigoTabular = ws.Cell(r, 7).GetValue<string>().Trim();
            var importe = ObtenerDecimal(ws, r, 8);   // "Suma de Importe"

            if (importe <= 0) { res.LineasIgnoradas++; continue; }

            res.TotalLineasArchivo++;
            lista.Add(new LineaRetencion(
                rfc, nombre, importe, "O", null,
                UnidadResponsable: codigoTabular,
                CURP: string.IsNullOrWhiteSpace(curp) ? null : curp));
        }

        return lista;
    }

    // ── Aplicar pago al préstamo activo del socio ────────────────────────────

    private async Task<bool> AplicarPago(
        int socioId, string rfc, LineaRetencion linea,
        int quincena, int anio, int encId, ResultadoImportacion res)
    {
        // Buscar TODAS las líneas de amortización del socio para ESTA quincena/año.
        // Regla: un pago de la quincena N solo puede aplicarse a la quincena N.
        // NUNCA se recorre hacia otra quincena — eso provocaba que el dinero de
        // una quincena cayera en quincenas que ni siquiera se han cargado
        // (p. ej. QNA 08/09 recibían importe sin existir en el historial).
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
        var detalle = detallesQna.FirstOrDefault(a => a.PagoQuincenalId == encId)
                   ?? detallesQna.FirstOrDefault(a => a.StatusPago == StatusPago.Pendiente);

        if (detalle is null)
        {
            // No hay amortización esperada para esta quincena. NO se inventa
            // aplicándola a otra: se reporta para que el auxiliar lo revise.
            res.SinAmortizacion++;
            res.Advertencias.Add($"{rfc} ({linea.Nombre}): sin amortización pendiente en QNA {quincena:D2}/{anio} (${linea.Importe:N2})");
            res.Log.Add($"[WARN] {rfc}: sin amortización pendiente en QNA {quincena:D2}/{anio} — NO se aplica a otra quincena.");
            return false;
        }

        detalle.MontoAplicado = linea.Importe;
        detalle.PagoQuincenalId = encId;
        detalle.StatusPago = linea.Importe == 0
            ? StatusPago.Suspendido
            : Math.Abs(linea.Importe - detalle.Pago) <= 1.0m
                ? StatusPago.Aplicado
                : StatusPago.Diferencia;

        res.CreditosAplicados++;
        res.ImporteTotal += linea.Importe;
        return true;
    }

    // ── Confronta (reconciliación universo ↔ créditos) ───────────────────────

    public class ResultadoConfronta
    {
        public int LineasRevisadas { get; set; }
        public int PagosAplicados { get; set; }
        public int ConDiferencia { get; set; }
        public int SinCreditoActivo { get; set; }
        public int SinAmortizacionQna { get; set; }
        public int Provisionales { get; set; }
        public decimal ImporteAplicado { get; set; }
        public List<string> Log { get; set; } = new();
        public List<string> Advertencias { get; set; } = new();
    }

    /// <summary>
    /// Confronta el universo de retenciones (RetencionLineas) contra los créditos
    /// activos y aplica los pagos a la amortización (MontoAplicado), SIN necesidad
    /// de reimportar el Excel. Resuelve el caso en que el pago entró al universo
    /// antes de que existiera el crédito o cuando el socio aún era Provisional.
    ///
    /// Es IDEMPOTENTE: sobrescribe el monto, no lo suma — se puede correr cuantas
    /// veces se quiera. Filtros opcionales (null = todo el universo):
    ///   quincena/anio  → solo esa quincena.
    ///   socioId        → reconfrontar un solo socio.
    /// </summary>
    public async Task<ResultadoConfronta> ConfrontarAsync(
        int? quincena = null, int? anio = null, int? socioId = null)
    {
        var res = new ResultadoConfronta();

        // Solo líneas que pertenecen a un socio registrado.
        var q = _db.RetencionLineas
            .Include(l => l.Encabezado)
            .Include(l => l.Socio)
            .Where(l => l.SocioId != null);

        if (quincena.HasValue) q = q.Where(l => l.Encabezado.Quincena == quincena.Value);
        if (anio.HasValue) q = q.Where(l => l.Encabezado.Anio == anio.Value);
        if (socioId.HasValue) q = q.Where(l => l.SocioId == socioId.Value);

        var lineas = await q.ToListAsync();
        res.LineasRevisadas = lineas.Count;

        foreach (var linea in lineas)
        {
            int qna = linea.Encabezado.Quincena;
            int ani = linea.Encabezado.Anio;

            // Socio provisional: aún no tiene crédito capturado.
            if (linea.Socio is { EstadoCaptura: EstadoCaptura.Provisional })
            {
                res.Provisionales++;
                linea.AplicadoACredito = false;
                continue;
            }

            // Amortización de la quincena EXACTA para un crédito activo del socio.
            var detallesQna = await _db.AmortizacionDetalles
                .Include(a => a.Prestamo)
                .Where(a => a.Prestamo.SocioId == linea.SocioId
                         && a.Prestamo.Status == StatusPrestamo.Activo
                         && a.Quincena == qna
                         && a.Anio == ani)
                .OrderBy(a => a.PrestamoId)
                .ToListAsync();

            if (detallesQna.Count == 0)
            {
                bool tieneCredito = await _db.Prestamos.AnyAsync(
                    p => p.SocioId == linea.SocioId && p.Status == StatusPrestamo.Activo);
                if (tieneCredito) res.SinAmortizacionQna++;
                else res.SinCreditoActivo++;

                res.Advertencias.Add(
                    $"{linea.RFC} ({linea.NombreEmpleado}): sin amortización en QNA {qna:D2}/{ani} (${linea.Importe:N2}).");
                linea.AplicadoACredito = false;
                continue;
            }

            // ── Elegir el crédito correcto ───────────────────────────────────
            // Si el socio tiene varios créditos activos en la misma quincena
            // (caso típico tras una REESTRUCTURA), no se aplica al primero a
            // ciegas: se elige el crédito cuyo PAGO coincide con el importe
            // retenido. Si ninguno coincide, se aplica al primero y queda
            // marcado como Diferencia para revisión.
            const decimal tol = 1.0m;

            var coincide = detallesQna
                .Where(a => Math.Abs(linea.Importe - a.Pago) <= tol)
                .OrderBy(a => a.PrestamoId)
                .FirstOrDefault();

            AmortizacionDetalle detalle;
            if (coincide is not null)
            {
                detalle = coincide;

                // Limpieza: si una corrida anterior dejó este pago marcado en
                // OTRO crédito del mismo socio como Diferencia (elección
                // equivocada), se resetea a Pendiente. Un crédito ya Aplicado
                // correctamente (p. ej. otra institución) NO se toca.
                foreach (var otro in detallesQna)
                {
                    if (ReferenceEquals(otro, detalle)) continue;
                    if (otro.StatusPago == StatusPago.Diferencia && otro.MontoAplicado.HasValue)
                    {
                        otro.MontoAplicado = null;
                        otro.PagoQuincenalId = null;
                        otro.StatusPago = StatusPago.Pendiente;
                        res.Log.Add($"[FIX] {linea.RFC}: se reubicó el pago de QNA {qna:D2}/{ani} al crédito #{detalle.PrestamoId} (coincide ${detalle.Pago:N2}); se limpió #{otro.PrestamoId}.");
                    }
                }
            }
            else
            {
                // Ninguno coincide en monto: determinista por PrestamoId
                // (idempotente entre corridas) y se marcará como Diferencia.
                detalle = detallesQna.OrderBy(a => a.PrestamoId).First();
            }

            // Trazabilidad: enlazar al encabezado de pagos de esa quincena, si existe.
            int? pqId = await _db.PagoQuincenalEncabezados
                .Where(e => e.Quincena == qna && e.Anio == ani)
                .OrderByDescending(e => e.FechaCarga)
                .Select(e => (int?)e.Id)
                .FirstOrDefaultAsync();

            detalle.MontoAplicado = linea.Importe;
            if (pqId.HasValue) detalle.PagoQuincenalId = pqId;
            detalle.StatusPago = linea.Importe == 0
                ? StatusPago.Suspendido
                : Math.Abs(linea.Importe - detalle.Pago) <= tol
                    ? StatusPago.Aplicado
                    : StatusPago.Diferencia;

            linea.AplicadoACredito = true;
            res.PagosAplicados++;
            res.ImporteAplicado += linea.Importe;
            if (detalle.StatusPago == StatusPago.Diferencia) res.ConDiferencia++;
        }

        await _db.SaveChangesAsync();

        res.Log.Add($"[OK] Líneas revisadas: {res.LineasRevisadas}");
        res.Log.Add($"[OK] Pagos aplicados: {res.PagosAplicados} (${res.ImporteAplicado:N2})");
        res.Log.Add($"[OK] Con diferencia de monto: {res.ConDiferencia}");
        res.Log.Add($"[OK] Socios provisionales (sin crédito aún): {res.Provisionales}");
        res.Log.Add($"[OK] Con crédito pero sin amortización en esa QNA: {res.SinAmortizacionQna}");
        res.Log.Add($"[OK] Sin crédito activo: {res.SinCreditoActivo}");
        return res;
    }

    // ── Tablero de cargas (pendientes vs. cargadas) ──────────────────────────

    public class QuincenaCargaCredito
    {
        public int Quincena { get; set; }
        public int Anio { get; set; }
        public bool EsQuincenaActual { get; set; }

        public bool SszCargado { get; set; }
        public decimal SszImporte { get; set; }
        public int SszLineas { get; set; }
        public DateTime? SszFecha { get; set; }

        public bool ImssCargado { get; set; }
        public decimal ImssImporte { get; set; }
        public int ImssLineas { get; set; }
        public DateTime? ImssFecha { get; set; }

        public decimal ImporteTotal => SszImporte + ImssImporte;
        public bool Completa => SszCargado && ImssCargado;
    }

    /// <summary>
    /// Tablero de las últimas <paramref name="quincenasHaciaAtras"/> quincenas
    /// (contando la actual, calculada de la fecha de hoy): para cada una,
    /// indica si ya se cargó el archivo de SSZ y el de IMSS Bienestar, con su
    /// importe, o si falta. Sirve para ver de un vistazo qué archivos hacen
    /// falta subir.
    /// </summary>
    public async Task<List<QuincenaCargaCredito>> ObtenerTableroCargasAsync(int quincenasHaciaAtras = 8)
    {
        var (qActual, aActual) = PrestamoService.QuincenaDeFecha(DateTime.Today);

        var quincenas = new List<(int q, int a)>();
        var (q, a) = (qActual, aActual);
        for (int i = 0; i < quincenasHaciaAtras; i++)
        {
            quincenas.Add((q, a));
            (q, a) = PrestamoService.AnteriorQuincena(q, a);
        }

        int anioMinimo = quincenas.Min(x => x.a);
        var encabezados = await _db.RetencionEncabezados
            .Where(e => e.Anio >= anioMinimo)
            .ToListAsync();

        var resultado = new List<QuincenaCargaCredito>();
        foreach (var (qq, aa) in quincenas)
        {
            var ssz = encabezados.FirstOrDefault(e => e.Quincena == qq && e.Anio == aa && e.Fuente == FuenteRetencion.Estatal);
            var imss = encabezados.FirstOrDefault(e => e.Quincena == qq && e.Anio == aa && e.Fuente == FuenteRetencion.Federal);

            resultado.Add(new QuincenaCargaCredito
            {
                Quincena = qq,
                Anio = aa,
                EsQuincenaActual = qq == qActual && aa == aActual,
                SszCargado = ssz is not null,
                SszImporte = ssz?.ImporteTotal ?? 0,
                SszLineas = ssz?.TotalRegistros ?? 0,
                SszFecha = ssz?.FechaCarga,
                ImssCargado = imss is not null,
                ImssImporte = imss?.ImporteTotal ?? 0,
                ImssLineas = imss?.TotalRegistros ?? 0,
                ImssFecha = imss?.FechaCarga,
            });
        }

        // Más reciente primero
        return resultado.OrderByDescending(r => r.Anio).ThenByDescending(r => r.Quincena).ToList();
    }

    // ── Historial ────────────────────────────────────────────────────────────

    /// <summary>¿Ya se cargó un archivo de créditos para esta quincena y esta
    /// institución (SSZ o IMSS Bienestar)? Se usa para avisar ANTES de
    /// reimportar (la reimportación es válida — actualiza los registros —
    /// pero debe ser a propósito, no por accidente).</summary>
    public async Task<PagoQuincenalEncabezado?> ObtenerCargaPreviaAsync(
        int quincena, int anio, bool esImssBienestar)
    {
        var candidatos = await _db.PagoQuincenalEncabezados
            .Where(e => e.Quincena == quincena && e.Anio == anio)
            .OrderByDescending(e => e.FechaCarga)
            .ToListAsync();

        return candidatos.FirstOrDefault(e =>
            e.Formato.ToUpper().Contains("IMSS") == esImssBienestar);
    }

    public async Task<List<PagoQuincenalEncabezado>> ObtenerHistorialAsync()
        => await _db.PagoQuincenalEncabezados
            .OrderByDescending(e => e.Anio)
            .ThenByDescending(e => e.Quincena)
            .ToListAsync();

    public class HistorialPaginado
    {
        public List<PagoQuincenalEncabezado> Items { get; set; } = new();
        public int TotalRegistros { get; set; }
        public int Pagina { get; set; } = 1;
        public int TamanoPagina { get; set; } = 15;
        public int TotalPaginas =>
            TamanoPagina > 0 ? (int)Math.Ceiling(TotalRegistros / (double)TamanoPagina) : 0;
    }

    /// <summary>
    /// Historial de archivos importados, paginado — con decenas de cargas
    /// acumuladas (una por quincena por institución) la lista completa se
    /// vuelve pesada de ver; se trae solo una página a la vez.
    /// </summary>
    public async Task<HistorialPaginado> ObtenerHistorialPaginadoAsync(int pagina = 1, int tamanoPagina = 15)
    {
        if (pagina < 1) pagina = 1;
        if (tamanoPagina < 1) tamanoPagina = 15;

        var query = _db.PagoQuincenalEncabezados
            .OrderByDescending(e => e.Anio)
            .ThenByDescending(e => e.Quincena)
            .ThenByDescending(e => e.FechaCarga);

        var total = await query.CountAsync();

        // Si al paginar (p. ej. tras borrar/filtrar) la página pedida quedó
        // fuera de rango, regresar a la última página válida en vez de una
        // lista vacía confusa.
        int totalPaginas = tamanoPagina > 0 ? (int)Math.Ceiling(total / (double)tamanoPagina) : 0;
        if (totalPaginas > 0 && pagina > totalPaginas) pagina = totalPaginas;

        var items = await query
            .Skip((pagina - 1) * tamanoPagina)
            .Take(tamanoPagina)
            .ToListAsync();

        return new HistorialPaginado
        {
            Items = items,
            TotalRegistros = total,
            Pagina = pagina,
            TamanoPagina = tamanoPagina
        };
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static (int quincena, int anio) ExtraerQuincenaAnio(string nombreHoja, List<string> log)
    {
        // Patrones: "QNA.  03  2025 TTIB", "QNA. 11/2025", "QNA. 03  2025"
        var match = Regex.Match(nombreHoja,
            @"QNA\.?\s*(\d{1,2})\s*[/\-]?\s*(20\d{2})",
            RegexOptions.IgnoreCase);

        if (match.Success
            && int.TryParse(match.Groups[1].Value, out int q)
            && int.TryParse(match.Groups[2].Value, out int a)
            && q >= 1 && q <= 24)
            return (q, a);

        log.Add($"[WARN] No se pudo extraer QNA de «{nombreHoja}» — usando QNA 1/{DateTime.Today.Year}");
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

    /// <summary>Recorta un texto al largo máximo de su columna para evitar
    /// el error de SQL Server "String or binary data would be truncated".</summary>
    private static string Trunc(string? s, int max)
    {
        s = (s ?? string.Empty).Trim();
        return s.Length <= max ? s : s[..max];
    }
}