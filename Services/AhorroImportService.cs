using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using CajaAhorro.Data;
using CajaAhorro.Models;
using CajaAhorro.Helpers;
using System.Text.RegularExpressions;

namespace CajaAhorro.Services;

/// <summary>
/// Importa archivos de descuentos de Fondo de Ahorro Sindical.
/// Soporta dos formatos (igual criterio que RetencionImportService para préstamos):
///   - SSZ: encabezado en fila 6, columnas RFC | NOMBRE | IMPORTE | VIGENCIA | TIPO NOMINA | TIPO DE EMP | UNIDAD RESPONS.
///   - IMSS BIENESTAR: encabezado en fila 5, columnas
///     RFC | CURP | APaterno | AMaterno | Nombres | NombreCto | Código Tabular | Suma de Importe.
///     Trae CURP (se usa para completar el dato en Socios si falta).
/// El formato se detecta automáticamente por la estructura de encabezados.
/// El ciclo (Navideño / Escolar) se detecta del título o el usuario lo elige.
/// Clave de unicidad del encabezado: Quincena + Anio + Ciclo.
/// </summary>
public class AhorroImportService
{
    private readonly AppDbContext _db;

    public AhorroImportService(AppDbContext db) => _db = db;

    // ── Resultado ─────────────────────────────────────────────────────────────

    public class ResultadoImportacion
    {
        public int Quincena { get; set; }
        public int Anio { get; set; }
        public CicloAhorro Ciclo { get; set; }
        public string CicloDetectado { get; set; } = "";   // texto "NAVIDEÑO" / "ESCOLAR" / ""
        public string PestanaLeida { get; set; } = "";
        public string TituloOriginal { get; set; } = "";

        public int TotalLineasArchivo { get; set; }
        public int AhorrosAplicados { get; set; }
        public int SociosNoEncontrados { get; set; }
        public int SociosProvisionales { get; set; }   // socios dados de alta automáticamente en esta importación
        public int LineasIgnoradas { get; set; }
        public decimal ImporteTotal { get; set; }
        public bool EsReimportacion { get; set; }
        public bool Exitoso { get; set; } = true;
        public string? Error { get; set; }

        public List<string> Log { get; set; } = new();
        public List<string> Advertencias { get; set; } = new();
    }

    // ── Análisis previo (pre-lectura sin importar) ────────────────────────────

    public class AnalisisArchivo
    {
        public int Quincena { get; set; }
        public int Anio { get; set; }
        public CicloAhorro? CicloDetectado { get; set; }
        public string NombrePestana { get; set; } = "";
        public string TituloOriginal { get; set; } = "";
        public string FormatoDetectado { get; set; } = "";
        public bool FormatoValido { get; set; }
        public string? ErrorFormato { get; set; }
        public List<string> Log { get; set; } = new();

        // El formato CAP Bienestar contiene dos ciclos y dos quincenas.
        // Su deteccion NO implica permiso de importacion: primero debe implementarse
        // la separacion por fuente para evitar sobrescritura de ahorros de SSZ.
        public bool EsCifrasBienestar { get; set; }
        public List<AhorroBienestarCifrasReader.Resumen> ResumenCifras { get; set; } = new();
    }

    /// <summary>
    /// Analiza el archivo sin importar: detecta quincena, año y ciclo.
    /// </summary>
    public static AnalisisArchivo Analizar(Stream excelStream)
    {
        var res = new AnalisisArchivo
        {
            Quincena = 1,
            Anio = DateTime.Today.Year,
        };

        try
        {
            using var wb = new XLWorkbook(excelStream);
            if (AhorroBienestarCifrasReader.EsFormato(wb))
            {
                res.FormatoDetectado = "IMSS BIENESTAR - CIFRAS CAP (AAFA/AAFB)";
                res.EsCifrasBienestar = true;
                res.ResumenCifras = AhorroBienestarCifrasReader.Analizar(wb);
                res.FormatoValido = true; // La validación de cada renglón depende de RFC e importe, no del total de la hoja.
                res.CicloDetectado = null; // Dos ciclos: no seleccionar uno arbitrariamente.
                res.Log.Add("[INFO] Archivo Bienestar CAP: 2 ciclos y 2 quincenas por archivo.");
                foreach (var detalleCifras in res.ResumenCifras)
                    res.Log.Add($"[OK] {detalleCifras.Hoja} {detalleCifras.Ciclo} QNA {detalleCifras.Quincena:D2}: {detalleCifras.RegistrosValidos} registros con RFC e importe, {detalleCifras.Importe:C}, {detalleCifras.FilasNoValidas} incidencias. El total de hoja no se compara.");
                return res;
            }
            var hoja = wb.Worksheet(1);

            res.NombrePestana = hoja.Name;
            res.Log.Add($"[INFO] Pestaña: «{hoja.Name}»");

            // Título completo (filas 1-5)
            for (int r = 1; r <= 5; r++)
            {
                var v = hoja.Cell(r, 1).GetValue<string>().Trim();
                if (v.Length > res.TituloOriginal.Length) res.TituloOriginal = v;
            }
            res.Log.Add($"[INFO] Título: {res.TituloOriginal}");

            // Detectar ciclo del título
            var tUP = res.TituloOriginal.ToUpper();
            if (tUP.Contains("NAVIDEÑO") || tUP.Contains("NAVIDENO"))
            {
                res.CicloDetectado = CicloAhorro.Navideño;
                res.Log.Add("[OK] Ciclo detectado: NAVIDEÑO");
            }
            else if (tUP.Contains("ESCOLAR"))
            {
                res.CicloDetectado = CicloAhorro.Escolar;
                res.Log.Add("[OK] Ciclo detectado: ESCOLAR");
            }
            else
            {
                res.Log.Add("[WARN] No se detectó ciclo en el título — elige manualmente.");
            }

            // Validar formato: SSZ (RFC/NOMBRE/IMPORTE) o IMSS Bienestar (RFC/CURP)
            bool columnasOk = false;
            for (int r = 1; r <= 10; r++)
            {
                var c1 = hoja.Cell(r, 1).GetValue<string>().Trim().ToUpper();
                var c2 = hoja.Cell(r, 2).GetValue<string>().Trim().ToUpper();
                var c3 = hoja.Cell(r, 3).GetValue<string>().Trim().ToUpper();

                if (c1 == "RFC" && c2 == "CURP")
                {
                    columnasOk = true;
                    res.FormatoDetectado = "IMSS BIENESTAR";
                    res.Log.Add($"[OK] Columnas RFC/CURP (IMSS Bienestar) en fila {r}");
                    break;
                }

                if (c1 == "RFC" && c2.Contains("NOMBRE") && c3.Contains("IMPORTE"))
                {
                    columnasOk = true;
                    res.FormatoDetectado = "SSZ";
                    res.Log.Add($"[OK] Columnas RFC/NOMBRE/IMPORTE (SSZ) en fila {r}");
                    break;
                }
            }

            if (!columnasOk)
            {
                res.FormatoValido = false;
                res.ErrorFormato = "No se reconoció el formato del archivo (ni SSZ ni IMSS Bienestar). Verifica el archivo.";
                res.Log.Add($"[ERROR] {res.ErrorFormato}");
                return res;
            }

            // Extraer quincena/año: primero del nombre de la pestaña; si no trae
            // (algunos archivos de IMSS Bienestar vienen en una sola hoja sin QNA
            // en el nombre), se busca en el título en su lugar.
            var m = Regex.Match(hoja.Name, @"QNA\.?\s*(\d{1,2})\s*[/\-]?\s*(20\d{2})",
                RegexOptions.IgnoreCase);
            if (!m.Success)
                m = Regex.Match(res.TituloOriginal, @"QNA\.?\s*(\d{1,2})\s*[/\-]?\s*(20\d{2})",
                    RegexOptions.IgnoreCase);

            if (m.Success && int.TryParse(m.Groups[1].Value, out int q)
                          && int.TryParse(m.Groups[2].Value, out int a)
                          && q >= 1 && q <= 24)
            {
                res.Quincena = q;
                res.Anio = a;
                res.Log.Add($"[OK] QNA {q:D2}/{a} detectada");
            }
            else
            {
                res.Log.Add("[WARN] No se detectó quincena — ajusta manualmente.");
            }

            res.FormatoValido = true;
        }
        catch (Exception ex)
        {
            res.FormatoValido = false;
            res.ErrorFormato = ex.Message;
            res.Log.Add($"[ERROR] {ex.Message}");
        }

        return res;
    }

    // ── Importación ───────────────────────────────────────────────────────────

    public async Task<ResultadoImportacion> ImportarAsync(
        Stream excelStream,
        string nombreArchivo,
        CicloAhorro ciclo,
        int quincena,
        int anio)
    {
        var res = new ResultadoImportacion
        {
            Quincena = quincena,
            Anio = anio,
            Ciclo = ciclo,
        };

        try
        {
            using var wb = new XLWorkbook(excelStream);
            if (AhorroBienestarCifrasReader.EsFormato(wb))
                throw new InvalidOperationException("Este libro Bienestar CAP fue reconocido, pero su importacion requiere primero separar aportaciones por institucion en la base de datos. No se guardo ninguna retencion.");
            var hoja = wb.Worksheet(1);
            res.PestanaLeida = hoja.Name;

            // Obtener título
            for (int r = 1; r <= 5; r++)
            {
                var v = hoja.Cell(r, 1).GetValue<string>().Trim();
                if (v.Length > res.TituloOriginal.Length) res.TituloOriginal = v;
            }
            res.Log.Add($"[INFO] Pestaña: «{hoja.Name}» — QNA {quincena:D2}/{anio} — Ciclo: {ciclo}");

            // Detectar formato (mismo criterio que en Analizar)
            string formato = "SSZ";
            for (int r = 1; r <= 10; r++)
            {
                var c1 = hoja.Cell(r, 1).GetValue<string>().Trim().ToUpper();
                var c2 = hoja.Cell(r, 2).GetValue<string>().Trim().ToUpper();
                if (c1 == "RFC" && c2 == "CURP") { formato = "IMSS BIENESTAR"; break; }
                if (c1 == "RFC" && c2.Contains("NOMBRE")) { formato = "SSZ"; break; }
            }
            res.Log.Add($"[INFO] Formato: {formato}");

            // Leer líneas según formato
            var lineas = formato == "IMSS BIENESTAR"
                ? LeerLineasImssBienestar(hoja, res)
                : LeerLineasSSZ(hoja, res);
            res.Log.Add($"[INFO] Líneas válidas: {lineas.Count} — Importe total: {res.ImporteTotal:C}");

            // Socios indexados por RFC
            var sociosPorRfc = await _db.Socios
                .ToDictionaryAsync(s => s.RFC.ToUpper().Trim(), StringComparer.OrdinalIgnoreCase);

            // Upsert encabezado (uno por Quincena+Anio+Ciclo)
            var enc = await _db.AhorroEncabezados
                .FirstOrDefaultAsync(e => e.Quincena == quincena && e.Anio == anio && e.Ciclo == ciclo);

            if (enc is null)
            {
                enc = new AhorroEncabezado
                {
                    Quincena = quincena,
                    Anio = anio,
                    Ciclo = ciclo,
                    NombreArchivo = nombreArchivo,
                    TituloOriginal = res.TituloOriginal,
                    FechaCarga = DateTime.Now,
                    VecesImportado = 1,
                };
                _db.AhorroEncabezados.Add(enc);
                await _db.SaveChangesAsync();
                res.Log.Add($"[NUEVO] Encabezado creado Id={enc.Id}");
            }
            else
            {
                enc.VecesImportado++;
                enc.NombreArchivo = nombreArchivo;
                enc.TituloOriginal = res.TituloOriginal;
                enc.FechaUltimaActualizacion = DateTime.Now;
                res.EsReimportacion = true;
                res.Log.Add($"[UPDATE] Reimportación #{enc.VecesImportado} (Id={enc.Id})");
            }

            // ── Alta de socios PROVISIONALES ───────────────────────────────────
            // No se pierde ningún ahorrador: si el RFC trae dinero pero no está
            // capturado, se da de alta con los datos disponibles (RFC + nombre)
            // marcado como Provisional. Los demás datos se completan después.
            var provisionalesNuevos = new List<Socio>();
            var rfcsProcesados = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var linea in lineas)
            {
                if (sociosPorRfc.ContainsKey(linea.rfc)) continue;   // ya es socio
                if (!rfcsProcesados.Add(linea.rfc)) continue;   // RFC duplicado en el mismo archivo

                var prov = new Socio
                {
                    RFC = linea.rfc,
                    Nombre = string.IsNullOrWhiteSpace(linea.nombre)
                                        ? "(SIN NOMBRE)"
                                        : linea.nombre,
                    CURP = string.IsNullOrWhiteSpace(linea.curp) ? null : linea.curp.Trim().ToUpper(),
                    EstadoCaptura = EstadoCaptura.Provisional,
                    Activo = true,
                    FechaRegistro = DateTime.Now,
                };
                _db.Socios.Add(prov);
                provisionalesNuevos.Add(prov);
            }

            if (provisionalesNuevos.Count > 0)
            {
                // SaveChanges aquí asigna el Id a cada provisional ANTES de crear
                // los Ahorros, para poder usar solo el FK escalar (SocioId) y evitar
                // el problema de re-inserción por propiedad de navegación de EF.
                await _db.SaveChangesAsync();

                foreach (var p in provisionalesNuevos)
                    sociosPorRfc[p.RFC.ToUpper().Trim()] = p;

                res.SociosProvisionales = provisionalesNuevos.Count;
                res.Log.Add($"[NUEVO] Socios provisionales dados de alta: {provisionalesNuevos.Count}");
            }

            // Procesar cada línea
            foreach (var linea in lineas)
            {
                // Tras la pasada anterior, todo RFC válido ya existe como socio.
                if (!sociosPorRfc.TryGetValue(linea.rfc, out var socio))
                {
                    res.SociosNoEncontrados++;
                    res.Advertencias.Add($"RFC sin nombre/datos válidos, no se pudo dar de alta: {linea.rfc}");
                    continue;
                }

                // Completar CURP si el socio no la tenía y este archivo sí la trae.
                if (string.IsNullOrWhiteSpace(socio.CURP) && !string.IsNullOrWhiteSpace(linea.curp))
                {
                    socio.CURP = linea.curp.Trim().ToUpper();
                }

                // Upsert Ahorro (clave: SocioId + Quincena + Anio + Ciclo)
                var ahorro = await _db.Ahorros.FirstOrDefaultAsync(
                    a => a.SocioId == socio.Id
                      && a.Quincena == quincena
                      && a.Anio == anio
                      && a.Ciclo == ciclo);

                if (ahorro is null)
                {
                    ahorro = new Ahorro
                    {
                        SocioId = socio.Id,
                        Quincena = quincena,
                        Anio = anio,
                        Ciclo = ciclo,
                        MontoRetenido = linea.importe,
                        Status = StatusAhorro.Aplicado,
                        FechaRegistro = DateTime.Now,
                        AhorroEncabezadoId = enc.Id,
                    };
                    _db.Ahorros.Add(ahorro);
                }
                else
                {
                    ahorro.MontoRetenido = linea.importe;
                    ahorro.Status = StatusAhorro.Aplicado;
                    ahorro.AhorroEncabezadoId = enc.Id;
                }

                res.AhorrosAplicados++;
            }

            // Actualizar totales del encabezado
            enc.TotalRegistros = res.TotalLineasArchivo;
            enc.SociosEncontrados = res.AhorrosAplicados - res.SociosProvisionales; // ya existían
            enc.SociosNoEncontrados = res.SociosProvisionales;                         // se crearon provisionales
            enc.ImporteTotal = res.ImporteTotal;

            await _db.SaveChangesAsync();

            res.Log.Add($"[OK] Ahorros aplicados: {res.AhorrosAplicados}  ($ {res.ImporteTotal:N2})");
            res.Log.Add($"[OK] Provisionales creados: {res.SociosProvisionales}");
        }
        catch (Exception ex)
        {
            res.Exitoso = false;
            res.Error = ex.Message;
            res.Log.Add($"[ERROR FATAL] {ex.Message}");
        }

        return res;
    }

    // ── Leer líneas del Excel (formato SSZ) ──────────────────────────────────

    private List<(string rfc, string nombre, decimal importe, string? curp)> LeerLineasSSZ(
        IXLWorksheet ws, ResultadoImportacion res)
    {
        var lista = new List<(string, string, decimal, string?)>();

        // Buscar fila de encabezado (donde col A = "RFC")
        int filaEnc = 6;
        for (int r = 1; r <= 12; r++)
        {
            if (ws.Cell(r, 1).GetValue<string>().Trim().ToUpper() == "RFC")
            { filaEnc = r; break; }
        }

        res.Log.Add($"[INFO] Encabezados en fila {filaEnc}, datos desde fila {filaEnc + 1}");

        int lastRow = ws.LastRowUsed()?.RowNumber() ?? filaEnc;
        decimal total = 0;

        for (int r = filaEnc + 1; r <= lastRow; r++)
        {
            var rfc = ws.Cell(r, 1).GetValue<string>().Trim().ToUpper();

            // Saltar filas inválidas
            if (string.IsNullOrWhiteSpace(rfc) || rfc.Length < 12
                || rfc == "RFC" || rfc.StartsWith("TOTAL", StringComparison.OrdinalIgnoreCase)
                || rfc.StartsWith("<"))
            {
                res.LineasIgnoradas++;
                continue;
            }

            var nombre = TextoNomina.LimpiarNombre(ws.Cell(r, 2).GetValue<string>());
            var importe = ObtenerDecimal(ws, r, 3);

            if (importe <= 0)
            {
                res.LineasIgnoradas++;
                continue;
            }

            total += importe;
            res.TotalLineasArchivo++;
            lista.Add((rfc, nombre, importe, null));
        }

        res.ImporteTotal = total;
        return lista;
    }

    // ── Leer líneas del Excel (formato IMSS Bienestar) ───────────────────────
    // Estructura: encabezado en fila 5 —
    //   RFC | CURP | APaterno | Amaterno | Nombres | NombreCto | Código Tabular | Suma de Importe
    // (mismo formato que RetencionImportService.LeerImssB_Prestamos, pero para
    // descuentos de ahorro en vez de préstamos). Trae CURP: se usa para
    // completar el dato en Socios si aún no la tiene capturada.

    private List<(string rfc, string nombre, decimal importe, string? curp)> LeerLineasImssBienestar(
        IXLWorksheet ws, ResultadoImportacion res)
    {
        var lista = new List<(string, string, decimal, string?)>();

        // Buscar fila de encabezado: RFC en col 1 Y CURP en col 2
        int filaEnc = 5;
        for (int r = 1; r <= 10; r++)
        {
            if (ws.Cell(r, 1).GetValue<string>().Trim().ToUpper() == "RFC"
                && ws.Cell(r, 2).GetValue<string>().Trim().ToUpper() == "CURP")
            { filaEnc = r; break; }
        }

        res.Log.Add($"[INFO] Encabezados en fila {filaEnc}, datos desde fila {filaEnc + 1}");

        int lastRow = ws.LastRowUsed()?.RowNumber() ?? filaEnc;
        decimal total = 0;

        for (int r = filaEnc + 1; r <= lastRow; r++)
        {
            var rfc = ws.Cell(r, 1).GetValue<string>().Trim().ToUpper();

            if (string.IsNullOrWhiteSpace(rfc) || rfc.Length < 12
                || rfc == "RFC" || rfc.StartsWith("TOTAL", StringComparison.OrdinalIgnoreCase))
            {
                res.LineasIgnoradas++;
                continue;
            }

            var curp = ws.Cell(r, 2).GetValue<string>().Trim().ToUpper();
            var aPaterno = ws.Cell(r, 3).GetValue<string>().Trim();
            var aMaterno = ws.Cell(r, 4).GetValue<string>().Trim();
            var nombres = ws.Cell(r, 5).GetValue<string>().Trim();
            var nombre = TextoNomina.LimpiarNombre($"{aPaterno} {aMaterno} {nombres}".Trim());

            // Columna 8 = "Suma de Importe"
            var importe = ObtenerDecimal(ws, r, 8);

            if (importe <= 0)
            {
                res.LineasIgnoradas++;
                continue;
            }

            total += importe;
            res.TotalLineasArchivo++;
            lista.Add((rfc, nombre, importe, string.IsNullOrWhiteSpace(curp) ? null : curp));
        }

        res.ImporteTotal = total;
        return lista;
    }

    // ── Historial ────────────────────────────────────────────────────────────

    // ── Definición de los ciclos ─────────────────────────────────────────────
    //
    //   NAVIDEÑO: enero a diciembre del mismo año  → QNA 01/A a QNA 24/A
    //   ESCOLAR:  julio del año A a junio del año A+1
    //             → QNA 13/A a QNA 24/A, luego QNA 01/(A+1) a QNA 12/(A+1)
    //
    // El "año" que se usa como filtro SIEMPRE es el año en que empieza el
    // ciclo (para navideño coincide con el año calendario; para escolar es
    // el año de julio). Estos helpers evitan repetir esa lógica en cada
    // consulta.

    /// <summary>¿Esta quincena/año cae dentro del ciclo que empieza en <paramref name="anioCiclo"/>?</summary>
    public static bool PerteneceAlCiclo(CicloAhorro ciclo, int anioCiclo, int anioRegistro, int quincenaRegistro)
    {
        if (ciclo == CicloAhorro.Navideño)
            return anioRegistro == anioCiclo;

        // Escolar: jul(A)-dic(A) es QNA 13-24 del año A; ene(A+1)-jun(A+1) es QNA 1-12 del año A+1.
        return (anioRegistro == anioCiclo && quincenaRegistro >= 13)
            || (anioRegistro == anioCiclo + 1 && quincenaRegistro <= 12);
    }

    /// <summary>Historial de encabezados de un año/ciclo. El año se interpreta como el
    /// año en que EMPIEZA cada ciclo (ver PerteneceAlCiclo): para escolar, el
    /// resultado incluye también las quincenas de enero-junio del año siguiente.</summary>
    public async Task<List<AhorroEncabezado>> ObtenerHistorialAsync(int? anioCiclo = null)
    {
        var q = _db.AhorroEncabezados.AsQueryable();

        if (anioCiclo.HasValue)
        {
            int a = anioCiclo.Value;
            q = q.Where(e =>
                (e.Ciclo == CicloAhorro.Navideño && e.Anio == a) ||
                (e.Ciclo == CicloAhorro.Escolar &&
                    ((e.Anio == a && e.Quincena >= 13) || (e.Anio == a + 1 && e.Quincena <= 12))));
        }

        return await q
            .OrderByDescending(e => e.Anio)
            .ThenByDescending(e => e.Quincena)
            .ThenBy(e => e.Ciclo)
            .ToListAsync();
    }

    /// <summary>Resumen por ciclo del año indicado (año en que empieza cada ciclo).</summary>
    public async Task<List<ResumenCiclo>> ObtenerResumenAnioAsync(int anioCiclo)
    {
        var todos = await _db.AhorroEncabezados
            .Where(e =>
                (e.Ciclo == CicloAhorro.Navideño && e.Anio == anioCiclo) ||
                (e.Ciclo == CicloAhorro.Escolar &&
                    ((e.Anio == anioCiclo && e.Quincena >= 13) || (e.Anio == anioCiclo + 1 && e.Quincena <= 12))))
            .ToListAsync();

        return todos
            .GroupBy(e => e.Ciclo)
            .Select(g => new ResumenCiclo
            {
                Ciclo = g.Key,
                Quincenas = g.Count(),
                TotalImporte = g.Sum(e => e.ImporteTotal),
                TotalSocios = g.Max(e => e.SociosEncontrados),
            })
            .ToList();
    }

    // ── Tablero de cargas (pendientes vs. cargadas) ──────────────────────────

    /// <summary>¿Ya se cargó un archivo de ahorro para esta quincena/ciclo?
    /// Para avisar ANTES de reimportar y que sea a propósito, no por accidente.</summary>
    public async Task<AhorroEncabezado?> ObtenerCargaPreviaAsync(int quincena, int anio, CicloAhorro ciclo)
        => await _db.AhorroEncabezados
            .FirstOrDefaultAsync(e => e.Quincena == quincena
                                   && e.Anio == anio
                                   && e.Ciclo == ciclo);

    /// <summary>¿Este socio tiene al menos un registro de ahorro? Se usa para
    /// sugerir la tasa de interés (2.5%) al otorgar un préstamo nuevo.</summary>
    public async Task<bool> EsAhorradorAsync(int socioId)
        => await _db.Ahorros.AnyAsync(a => a.SocioId == socioId);

    /// <summary>Total acumulado de ahorro del socio (todos los ciclos). Se usa
    /// para el finiquito con ahorro: saber si ya juntó el monto que pidió
    /// destinar a terminar de pagar su crédito.</summary>
    public async Task<decimal> TotalAhorradoAsync(int socioId)
        => await _db.Ahorros
            .Where(a => a.SocioId == socioId)
            .SumAsync(a => (decimal?)a.MontoRetenido) ?? 0m;

    public class QuincenaCargaAhorro
    {
        public int Quincena { get; set; }
        public int Anio { get; set; }
        public bool EsQuincenaActual { get; set; }

        public bool NavidenoCargado { get; set; }
        public decimal NavidenoImporte { get; set; }
        public DateTime? NavidenoFecha { get; set; }

        public bool EscolarCargado { get; set; }
        public decimal EscolarImporte { get; set; }
        public DateTime? EscolarFecha { get; set; }

        public decimal ImporteTotal => NavidenoImporte + EscolarImporte;
        public bool Completa => NavidenoCargado && EscolarCargado;
    }

    /// <summary>
    /// Tablero de las últimas <paramref name="quincenasHaciaAtras"/> quincenas
    /// (contando la actual): para cada una, indica si ya se cargó el archivo
    /// de ahorro Navideño y el Escolar, con su importe, o si falta. Ambos
    /// ciclos corren en paralelo TODO el año (por eso siempre se esperan los
    /// dos archivos en cada quincena, aunque tengan "año de ciclo" distinto).
    /// </summary>
    public async Task<List<QuincenaCargaAhorro>> ObtenerTableroCargasAsync(int quincenasHaciaAtras = 8)
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
        var encabezados = await _db.AhorroEncabezados
            .Where(e => e.Anio >= anioMinimo)
            .ToListAsync();

        var resultado = new List<QuincenaCargaAhorro>();
        foreach (var (qq, aa) in quincenas)
        {
            var nav = encabezados.FirstOrDefault(e => e.Quincena == qq && e.Anio == aa && e.Ciclo == CicloAhorro.Navideño);
            var esc = encabezados.FirstOrDefault(e => e.Quincena == qq && e.Anio == aa && e.Ciclo == CicloAhorro.Escolar);

            resultado.Add(new QuincenaCargaAhorro
            {
                Quincena = qq,
                Anio = aa,
                EsQuincenaActual = qq == qActual && aa == aActual,
                NavidenoCargado = nav is not null,
                NavidenoImporte = nav?.ImporteTotal ?? 0,
                NavidenoFecha = nav?.FechaCarga,
                EscolarCargado = esc is not null,
                EscolarImporte = esc?.ImporteTotal ?? 0,
                EscolarFecha = esc?.FechaCarga,
            });
        }

        return resultado.OrderByDescending(r => r.Anio).ThenByDescending(r => r.Quincena).ToList();
    }

    public class ResumenCiclo
    {
        public CicloAhorro Ciclo { get; set; }
        public int Quincenas { get; set; }
        public decimal TotalImporte { get; set; }
        public int TotalSocios { get; set; }
    }

    // ── Helper ───────────────────────────────────────────────────────────────

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
