using Microsoft.EntityFrameworkCore;
using ClosedXML.Excel;
using CajaAhorro.Data;
using CajaAhorro.Models;
using System.Text.RegularExpressions;

namespace CajaAhorro.Services;

public class CargaInicialService
{
    private readonly AppDbContext _db;
    private readonly PrestamoService _prestamoService;

    public CargaInicialService(AppDbContext db, PrestamoService prestamoService)
    {
        _db = db;
        _prestamoService = prestamoService;
    }

    public class ResultadoCarga
    {
        public int SociosCreados { get; set; }
        public int PrestamosCreados { get; set; }
        public int FilasOmitidas { get; set; }
        public List<string> Errores { get; set; } = new();
        public bool Exitoso => !Errores.Any();
    }

    // ── Columnas ─────────────────────────────────────────────────────────────
    // El archivo de carga NO siempre trae las mismas columnas en el mismo
    // orden entre entregas (p. ej. en 2026 desapareció la columna "No.
    // Cheque", recorriendo todo lo demás una posición). Por eso las columnas
    // se resuelven por NOMBRE DE ENCABEZADO (fila 1) en vez de por número fijo
    // — así el importador no se rompe silenciosamente si el archivo cambia.

    public async Task<ResultadoCarga> CargarPrestamos2025Async(Stream archivoExcel, List<string> log)
    {
        var resultado = new ResultadoCarga();
        var sociosCache = new Dictionary<string, Socio>(StringComparer.OrdinalIgnoreCase);

        // Caché por RFC: el índice único de Socios.RFC truena si el archivo trae
        // el mismo RFC con dos escrituras distintas del nombre. En ese caso se
        // REUTILIZA el socio ya creado en vez de intentar un segundo INSERT.
        var rfcCache = new Dictionary<string, Socio>(StringComparer.OrdinalIgnoreCase);

        var sociosExistentes = await _db.Socios.ToListAsync();
        foreach (var s in sociosExistentes)
        {
            sociosCache[s.Nombre.ToUpper().Trim()] = s;
            rfcCache[s.RFC.ToUpper().Trim()] = s;
        }

        log.Add($"[INFO] Socios existentes en BD: {sociosExistentes.Count}");

        using var wb = new XLWorkbook(archivoExcel);
        var ws = wb.Worksheet(1);

        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
        log.Add($"[INFO] Última fila usada: {lastRow}");

        // ── Resolver columnas por encabezado (fila 1) ───────────────────────
        var encabezados = LeerEncabezados(ws);

        int Buscar(string[] exactos, string[]? contieneTodos = null)
            => BuscarColumna(encabezados, exactos, contieneTodos);

        int colTipo        = Buscar(new[] { "TIPO DE PRESTAMO" }, new[] { "TIPO" });
        int colBase         = Buscar(new[] { "BASE" });
        int colQnaUltimo    = Buscar(new[] { "QNA. ULTIMO DCTO." }, new[] { "ULTIMO" });
        int colQna1er       = Buscar(new[] { "QNA. 1ER. DCTO." }, new[] { "1ER" });
        int colRfc          = Buscar(new[] { "R.F.C." }, new[] { "RFC" });
        int colNombre       = Buscar(new[] { "NOMBRE" });
        int colFechaSol     = Buscar(new[] { "FECHA SOLICITUD" }, new[] { "SOLICITUD" });
        int colImpSolic     = Buscar(new[] { "IMPORTE SOLICITADO" });
        int colImpPrestamo  = Buscar(new[] { "IMPORTE PRESTAMO" }, new[] { "IMPORTE", "PRESTAMO" });
        int colTransfFlag   = Buscar(new[] { "TRANSFERENCIA" });
        int colRefTransf    = Buscar(new[] { "REFERENCIA TRANSF." }, new[] { "REFERENCIA" });

        // Opcional: puede no existir (desapareció del archivo 2026).
        int colNoCheque     = Buscar(new[] { "NO. CHEQUE", "NO CHEQUE", "NÚMERO DE CHEQUE", "NUMERO DE CHEQUE" });

        int colImpChequeTransf = Buscar(new[] {
            "IMPORTE DEL CHEQUE", "IMPORTE DE LA TRANFERENCIA", "IMPORTE DE LA TRANSFERENCIA",
            "IMPORTE DEL CHEQUE O LA TRANFERENCIA", "IMPORTE DEL CHEQUE O LA TRANSFERENCIA" });
        if (colImpChequeTransf < 0) colImpChequeTransf = Buscar(Array.Empty<string>(), new[] { "IMPORTE", "CHEQUE" });
        if (colImpChequeTransf < 0) colImpChequeTransf = Buscar(Array.Empty<string>(), new[] { "IMPORTE", "TRANF" });

        int colFechaChequeTransf = Buscar(new[] {
            "FECHA DEL CHEQUE", "FECHA DE LA TRANFERENCIA", "FECHA DE LA TRANSFERENCIA",
            "FECHA DEL CHEQUE O DE LA TRANFERENCIA", "FECHA DEL CHEQUE O DE LA TRANSFERENCIA" });
        if (colFechaChequeTransf < 0) colFechaChequeTransf = Buscar(Array.Empty<string>(), new[] { "FECHA", "CHEQUE" });
        if (colFechaChequeTransf < 0) colFechaChequeTransf = Buscar(Array.Empty<string>(), new[] { "FECHA", "TRANF" });

        int colStatus       = Buscar(new[] { "STATUS" });
        int colFechaIni     = Buscar(new[] { "FECHA" });    // EXACTO — no confundir con "FECHA SOLICITUD"/"FECHA DE LA TRANSFERENCIA"
        int colObserv       = Buscar(new[] { "OBSERVACIONES" });
        int colPagoQna      = Buscar(new[] { "PAGO QUINCENAL" }, new[] { "PAGO", "QUINCENAL" });

        log.Add("[DEBUG] Columnas resueltas por encabezado: " +
                $"Tipo={colTipo} Base={colBase} QnaUltimo={colQnaUltimo} Qna1er={colQna1er} " +
                $"RFC={colRfc} Nombre={colNombre} FechaSolicitud={colFechaSol} " +
                $"ImpSolic={colImpSolic} ImpPrestamo={colImpPrestamo} TransfFlag={colTransfFlag} " +
                $"RefTransf={colRefTransf} NoCheque={colNoCheque} ImpChequeTransf={colImpChequeTransf} " +
                $"FechaChequeTransf={colFechaChequeTransf} Status={colStatus} FechaInicio={colFechaIni} " +
                $"Observaciones={colObserv} PagoQuincenal={colPagoQna}");

        // Columnas sin las que NO se puede procesar el archivo con confianza.
        var faltantesCriticas = new List<string>();
        if (colNombre < 0) faltantesCriticas.Add("NOMBRE");
        if (colRfc < 0) faltantesCriticas.Add("R.F.C.");
        if (colImpPrestamo < 0) faltantesCriticas.Add("IMPORTE PRESTAMO");
        if (colPagoQna < 0) faltantesCriticas.Add("PAGO QUINCENAL");
        if (colQna1er < 0) faltantesCriticas.Add("QNA. 1er. DCTO.");
        if (colQnaUltimo < 0) faltantesCriticas.Add("QNA. ULTIMO DCTO.");

        if (faltantesCriticas.Any())
        {
            resultado.Errores.Add(
                "No se encontraron estas columnas obligatorias en el archivo " +
                $"(revisa los encabezados de la fila 1): {string.Join(", ", faltantesCriticas)}");
            log.Add($"[ERROR] Columnas críticas no encontradas: {string.Join(", ", faltantesCriticas)}. Se aborta la carga.");
            return resultado;
        }
        if (colNoCheque < 0)
            log.Add("[AVISO] No se encontró columna de 'No. Cheque' — se asume que este archivo no la trae (normal si todo es transferencia).");

        int filasConDatos = 0;

        // Iterar por número de fila absoluto — saltando fila 1 (encabezado)
        for (int rowNum = 2; rowNum <= lastRow; rowNum++)
        {
            try
            {
                var nombre = ws.Cell(rowNum, colNombre).GetValue<string>()?.Trim().ToUpper();

                // Saltar filas vacías o sin nombre
                if (string.IsNullOrWhiteSpace(nombre))
                {
                    resultado.FilasOmitidas++;
                    continue;
                }

                var tipoCrudo = colTipo > 0 ? ws.Cell(rowNum, colTipo).GetValue<string>()?.Trim() : null;
                var base_ = colBase > 0 ? ws.Cell(rowNum, colBase).GetValue<string>()?.Trim() : null;
                var qnaUltimo = ws.Cell(rowNum, colQnaUltimo).GetValue<string>()?.Trim();
                var qna1er = ws.Cell(rowNum, colQna1er).GetValue<string>()?.Trim();
                var rfc = ws.Cell(rowNum, colRfc).GetValue<string>()?.Trim();

                // Fecha solicitud
                DateTime fechaSol = DateTime.Today;
                if (colFechaSol > 0)
                {
                    var celdaFecha = ws.Cell(rowNum, colFechaSol);
                    if (!celdaFecha.IsEmpty())
                    {
                        if (celdaFecha.Value.IsDateTime)
                            fechaSol = celdaFecha.Value.GetDateTime();
                        else if (DateTime.TryParse(celdaFecha.GetValue<string>(), out var fd))
                            fechaSol = fd;
                    }
                }

                // Importes
                decimal impSolic = colImpSolic > 0 ? ObtenerDecimal(ws, rowNum, colImpSolic) : 0;
                decimal impPrestamo = ObtenerDecimal(ws, rowNum, colImpPrestamo);

                if (impPrestamo <= 0)
                {
                    resultado.FilasOmitidas++;
                    continue;
                }

                filasConDatos++;

                var esTransfStr = colTransfFlag > 0 ? ws.Cell(rowNum, colTransfFlag).GetValue<string>()?.Trim() : null;
                var refTransf = colRefTransf > 0 ? ws.Cell(rowNum, colRefTransf).GetValue<string>()?.Trim() : null;
                var noCheque = colNoCheque > 0 ? ws.Cell(rowNum, colNoCheque).GetValue<string>()?.Trim() : null;
                decimal impCheque = colImpChequeTransf > 0 ? ObtenerDecimal(ws, rowNum, colImpChequeTransf) : 0;

                DateTime? fechaCheque = null;
                if (colFechaChequeTransf > 0)
                {
                    var celdaFechaCheq = ws.Cell(rowNum, colFechaChequeTransf);
                    if (!celdaFechaCheq.IsEmpty())
                    {
                        if (celdaFechaCheq.Value.IsDateTime)
                            fechaCheque = celdaFechaCheq.Value.GetDateTime();
                        else if (DateTime.TryParse(celdaFechaCheq.GetValue<string>(), out var fc))
                            fechaCheque = fc;
                    }
                }

                var statusCrudo = colStatus > 0 ? ws.Cell(rowNum, colStatus).GetValue<string>()?.Trim() : null;
                var observ = colObserv > 0 ? ws.Cell(rowNum, colObserv).GetValue<string>()?.Trim() : null;
                decimal pagoQna = ObtenerDecimal(ws, rowNum, colPagoQna);

                // ── Quincenas ────────────────────────────────────────────────
                // El archivo trae explícita la quincena del 1er y del último descuento:
                //   QNA. 1er. DCTO.   → normalmente solo el número de quincena (QQ),
                //                        pero en casos especiales trae "QQ/AAAA".
                //   QNA. ULTIMO DCTO. → AAAAQQ (año + quincena)
                // El AÑO de inicio por defecto se toma de la columna "FECHA" (la
                // fecha en que arranca el descuento, que puede diferir de la
                // fecha de solicitud), pero si "QNA. 1er. DCTO." trae también el
                // año explícito ("QQ/AAAA") ESE manda, por ser la fuente más
                // específica.
                int anioInicial = fechaSol.Year;            // respaldo
                if (colFechaIni > 0)
                {
                    var celdaFechaIni = ws.Cell(rowNum, colFechaIni);
                    if (!celdaFechaIni.IsEmpty())
                    {
                        if (celdaFechaIni.Value.IsDateTime)
                            anioInicial = celdaFechaIni.Value.GetDateTime().Year;
                        else if (DateTime.TryParse(celdaFechaIni.GetValue<string>(), out var fi))
                            anioInicial = fi.Year;
                    }
                }

                int quincenaInicial = 1;

                if (!string.IsNullOrEmpty(qna1er))
                {
                    if (qna1er.Contains('/'))
                    {
                        var partes = qna1er.Split('/');
                        if (partes.Length == 2
                            && int.TryParse(partes[0].TrimStart('0'), out int qi2) && qi2 >= 1 && qi2 <= 24
                            && int.TryParse(partes[1], out int ai2) && ai2 >= 2000)
                        {
                            quincenaInicial = qi2;
                            anioInicial = ai2;
                        }
                    }
                    else if (int.TryParse(qna1er.TrimStart('0'), out int qi) && qi >= 1 && qi <= 24)
                    {
                        quincenaInicial = qi;
                    }
                }

                int anioFinal = anioInicial;
                int quincenaFinal = quincenaInicial;
                if (!string.IsNullOrEmpty(qnaUltimo) && qnaUltimo.Length == 6
                    && int.TryParse(qnaUltimo[..4], out int af)
                    && int.TryParse(qnaUltimo[4..], out int qf) && qf >= 1 && qf <= 24)
                {
                    anioFinal = af;
                    quincenaFinal = qf;
                }

                // # de descuentos = quincenas entre el 1er y el último (24 por año),
                // inclusivo (+1). Confirmado con el archivo: da 24 para QNA 03 → 202602.
                int numQuincenas = (anioFinal - anioInicial) * 24
                                 + (quincenaFinal - quincenaInicial) + 1;
                if (numQuincenas < 1 || numQuincenas > 60)
                {
                    // La col FECHA dio un año inconsistente (p.ej. fecha basura):
                    // asumir el inicio más reciente que deje un plazo válido (~1 año).
                    anioInicial = quincenaFinal >= quincenaInicial ? anioFinal : anioFinal - 1;
                    numQuincenas = (anioFinal - anioInicial) * 24
                                 + (quincenaFinal - quincenaInicial) + 1;
                    if (numQuincenas < 1 || numQuincenas > 60) numQuincenas = 24;  // último recurso
                }

                // ── Pago ─────────────────────────────────────────────────────
                bool esTransf = (esTransfStr ?? "").ToUpper() == "SI";
                string? folioRef = esTransf ? refTransf : noCheque;
                if (folioRef == "N/A" || folioRef == "") folioRef = null;

                // ── Socio ────────────────────────────────────────────────────
                if (!sociosCache.TryGetValue(nombre, out var socio))
                {
                    var rfcNormalizado = NormalizarRFC(rfc, rowNum);

                    // Mismo RFC con otra escritura del nombre → reutilizar,
                    // NUNCA insertar un duplicado (índice único en Socios.RFC).
                    if (rfcCache.TryGetValue(rfcNormalizado, out var socioPorRfc))
                    {
                        socio = socioPorRfc;
                        sociosCache[nombre] = socio;
                        log.Add($"[AVISO] Fila {rowNum}: RFC {rfcNormalizado} ya existe como '{socio.Nombre}'; se reutiliza para '{nombre}'.");
                    }
                    else
                    {
                        var institucion = DeterminarInstitucion(base_);
                        socio = new Socio
                        {
                            Nombre = nombre,
                            RFC = rfcNormalizado,
                            TipoNombramiento = NormalizarNombramiento(base_),
                            JornadaLaboral = "",
                            Adscripcion = "",
                            InstitucionActual = institucion,
                            Activo = true,
                            FechaRegistro = DateTime.Now
                        };

                        _db.Socios.Add(socio);
                        await _db.SaveChangesAsync();

                        // Registrar institución inicial en el historial
                        _db.SocioInstituciones.Add(new SocioInstitucion
                        {
                            SocioId = socio.Id,
                            Institucion = institucion,
                            QuincenaInicio = quincenaInicial > 0 ? quincenaInicial : 1,
                            AnioInicio = anioInicial > 0 ? anioInicial : 2025,
                            FechaRegistro = DateTime.Now,
                            Observaciones = "Carga inicial 2025"
                        });
                        await _db.SaveChangesAsync();

                        sociosCache[nombre] = socio;
                        rfcCache[socio.RFC] = socio;
                        resultado.SociosCreados++;

                        if (resultado.SociosCreados <= 3)
                            log.Add($"[SOCIO] #{resultado.SociosCreados}: {nombre} RFC:{socio.RFC}");
                    }
                }

                // ── Préstamo ─────────────────────────────────────────────────
                // Idempotencia: si el archivo se vuelve a cargar (por error o a
                // propósito), NO crear un préstamo duplicado. Se considera el
                // mismo préstamo si coincide socio + importe otorgado + quincena
                // y año de inicio (la combinación que identifica una fila única
                // del archivo de carga inicial).
                bool yaExiste = await _db.Prestamos.AnyAsync(p =>
                    p.SocioId == socio.Id &&
                    p.ImporteOtorgado == impPrestamo &&
                    p.QuincenaInicial == quincenaInicial &&
                    p.AnioInicial == anioInicial);

                if (yaExiste)
                {
                    resultado.FilasOmitidas++;
                    if (resultado.FilasOmitidas <= 5)
                        log.Add($"[OMITIDO] Fila {rowNum}: {nombre} ya tiene un préstamo idéntico " +
                                $"(${impPrestamo:N0}, QNA {quincenaInicial}/{anioInicial}) — no se duplica.");
                    continue;
                }

                var prestamo = new Prestamo
                {
                    SocioId = socio.Id,   // solo FK escalar, nunca la navegación
                    TipoPrestamo = NormalizarTipo(tipoCrudo),
                    ImporteSolicitado = impSolic,
                    ImporteOtorgado = impPrestamo,
                    TasaInteres = 0.03m,   // se corrige abajo con la tasa REAL derivada del pago
                    NumeroDescuentos = numQuincenas,
                    PagoQuincenal = pagoQna > 0 ? pagoQna : 0,
                    TotalAPagar = pagoQna > 0 ? Math.Round(pagoQna * numQuincenas, 2) : impPrestamo,
                    QuincenaInicial = quincenaInicial,
                    AnioInicial = anioInicial,
                    QuincenaFinal = quincenaFinal,
                    AnioFinal = anioFinal,
                    FechaSolicitud = fechaSol,
                    EsTransferencia = esTransf,
                    NumeroChequeFolio = folioRef,
                    FechaTransferencia = fechaCheque,
                    ImporteChequeFolio = impCheque > 0 ? impCheque : null,
                    Status = NormalizarStatus(statusCrudo),
                    Observaciones = string.IsNullOrEmpty(observ) ? null : observ,
                    FechaRegistro = DateTime.Now
                };

                // La carga inicial respeta el PAGO QUINCENAL capturado en el
                // archivo. Recalcular al 3% daría un pago distinto al real
                // para los ahorradores (préstamo al 2.5%). Si la fila no trae
                // pago capturado (o la columna no se pudo leer bien), se usa
                // el cálculo estándar como respaldo.
                var amortizacion = pagoQna > 0
                    ? _prestamoService.CalcularAmortizacionDesdePago(prestamo, pagoQna)
                    : _prestamoService.CalcularAmortizacion(prestamo);

                // La TasaInteres guardada debe reflejar la tasa REAL implícita
                // en el pago capturado (2.5% ahorradores, 3% el resto, o lo
                // que traiga el archivo) y no quedarse en el 3% fijo de
                // arriba — de lo contrario se ve mal en el listado de
                // préstamos y en los formatos (PDF/Excel), aunque el pago y
                // el total sí sean correctos.
                prestamo.TasaInteres = DerivarTasaInteres(
                    prestamo.TotalAPagar, prestamo.ImporteOtorgado, prestamo.NumeroDescuentos);

                // Folio automático (AAAAMM-NNNN), igual que los préstamos
                // creados desde la pantalla — así la carga inicial también
                // queda con folio en vez de quedarse en NULL.
                prestamo.Folio = await _prestamoService.GenerarFolioAsync(prestamo.FechaSolicitud);

                _db.Prestamos.Add(prestamo);
                await _db.SaveChangesAsync();

                foreach (var d in amortizacion) d.PrestamoId = prestamo.Id;
                _db.AmortizacionDetalles.AddRange(amortizacion);
                await _db.SaveChangesAsync();

                resultado.PrestamosCreados++;

                if (resultado.PrestamosCreados <= 3)
                    log.Add($"[PRESTAMO] #{resultado.PrestamosCreados}: {nombre} ${impPrestamo:N0} QNA{quincenaInicial}/{anioInicial}→{quincenaFinal}/{anioFinal}");
            }
            catch (Exception ex)
            {
                resultado.Errores.Add($"Fila {rowNum}: {ex.InnerException?.Message ?? ex.Message}");
                if (resultado.Errores.Count <= 5)
                    log.Add($"[ERROR] Fila {rowNum}: {ex.InnerException?.Message ?? ex.Message}");

                // ── CRÍTICO: sanear el contexto ──────────────────────────────
                // Si un SaveChanges falló, las entidades quedan rastreadas en
                // estado Added dentro del DbContext (que en Blazor Server vive
                // TODA la sesión del navegador). Sin esta limpieza, CADA
                // SaveChanges posterior —el de la siguiente fila o el de
                // "Nuevo socio" después de la carga— vuelve a intentar esos
                // INSERT rotos y falla.
                foreach (var entry in _db.ChangeTracker.Entries()
                             .Where(e => e.State == EntityState.Added)
                             .ToList())
                {
                    entry.State = EntityState.Detached;
                }

                // Quitar de los cachés cualquier socio que no llegó a guardarse
                // (Id == 0): si se reutilizara, los préstamos saldrían con
                // SocioId = 0 y fallaría la FK.
                foreach (var k in sociosCache.Where(p => p.Value.Id == 0)
                                             .Select(p => p.Key).ToList())
                    sociosCache.Remove(k);
                foreach (var k in rfcCache.Where(p => p.Value.Id == 0)
                                          .Select(p => p.Key).ToList())
                    rfcCache.Remove(k);
            }
        }

        log.Add($"[INFO] Filas con datos procesadas: {filasConDatos}");
        return resultado;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>Lee la fila 1 y regresa un mapa ENCABEZADO_EN_MAYÚSCULAS → columna.</summary>
    private static Dictionary<string, int> LeerEncabezados(IXLWorksheet ws)
    {
        var mapa = new Dictionary<string, int>();
        int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 25;

        for (int c = 1; c <= lastCol; c++)
        {
            var texto = ws.Cell(1, c).GetValue<string>()?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(texto)) continue;

            // Colapsar espacios dobles/triples ("No.  Cheque" → "No. Cheque")
            // para que las comparaciones exactas no fallen por espaciado.
            texto = Regex.Replace(texto, @"\s+", " ");

            if (!mapa.ContainsKey(texto))
                mapa[texto] = c;
        }

        return mapa;
    }

    /// <summary>
    /// Busca una columna por encabezado: primero por coincidencia EXACTA
    /// (probando cada texto de <paramref name="exactos"/> en orden), y si
    /// ninguno aparece, por la primera columna cuyo encabezado contenga TODAS
    /// las palabras de <paramref name="contieneTodos"/>. Regresa -1 si no
    /// encuentra nada (columna ausente en este archivo).
    /// </summary>
    private static int BuscarColumna(Dictionary<string, int> encabezados, string[] exactos, string[]? contieneTodos = null)
    {
        foreach (var e in exactos)
            if (encabezados.TryGetValue(e, out var col)) return col;

        if (contieneTodos is { Length: > 0 })
        {
            foreach (var kv in encabezados)
                if (contieneTodos.All(p => kv.Key.Contains(p)))
                    return kv.Value;
        }

        return -1;
    }

    /// <summary>
    /// Deriva la tasa quincenal REAL implícita en el total a pagar ya
    /// calculado (a partir del pago capturado en el archivo), en vez de
    /// asumir un valor fijo. Usa el mismo modelo de interés plano que el
    /// resto del sistema: TotalAPagar = Importe + Importe × tasa × (plazo+2).
    /// Si el resultado cae muy cerca de una tasa conocida (2.5% o 3%) se
    /// ajusta a ese valor exacto para no guardar "casi 2.5%" por el redondeo
    /// de centavos del pago capturado.
    /// </summary>
    private static decimal DerivarTasaInteres(decimal totalAPagar, decimal importeOtorgado, int numeroDescuentos)
    {
        if (importeOtorgado <= 0 || numeroDescuentos <= 0) return 0.03m;   // respaldo

        int plazoCobro = numeroDescuentos + 2;
        decimal tasa = (totalAPagar - importeOtorgado) / (importeOtorgado * plazoCobro);

        if (Math.Abs(tasa - 0.025m) < 0.001m) return 0.025m;
        if (Math.Abs(tasa - 0.03m) < 0.001m) return 0.03m;

        return Math.Round(tasa, 4);
    }

    private static decimal ObtenerDecimal(IXLWorksheet ws, int row, int col)
    {
        if (col <= 0) return 0;
        var cell = ws.Cell(row, col);
        if (cell.IsEmpty()) return 0;
        var v = cell.Value;
        if (v.IsNumber) return (decimal)v.GetNumber();
        if (decimal.TryParse(v.ToString()?.Replace(",", ""), out var d)) return d;
        return 0;
    }

    private static TipoPrestamo NormalizarTipo(string? tipo)
        => (!string.IsNullOrEmpty(tipo) && tipo.ToUpper().Contains("REESTRUCTURA"))
            ? TipoPrestamo.Reestructura : TipoPrestamo.Ordinario;

    private static StatusPrestamo NormalizarStatus(string? status)
    {
        var s = (status ?? "").ToUpper().Trim().Replace("  ", " ");
        if (s.Contains("COBRADO")) return StatusPrestamo.Liquidado;
        if (s.Contains("RECHAZAD")) return StatusPrestamo.Cancelado;
        return StatusPrestamo.Activo;
    }

    private static InstitucionTipo DeterminarInstitucion(string? base_)
    {
        var b = (base_ ?? "").ToUpper().Trim();
        return (b == "FEDERAL" || b.StartsWith("FO") || b == "HOM")
            ? InstitucionTipo.Federal : InstitucionTipo.Estatal;
    }

    private static string NormalizarNombramiento(string? base_) =>
        (base_ ?? "").ToUpper().Trim() switch
        {
            "REG" => "Regularizado",
            "FEDERAL" => "Federal",
            "FO2" => "Formalizado",
            "FO3" => "Formalizado",
            "FOR" => "Formalizado",
            "HOM" => "Homologado",
            _ => "Federal"
        };

    private static string NormalizarRFC(string? rfc, int contador)
    {
        var r = rfc?.Trim().ToUpper();
        return (!string.IsNullOrEmpty(r) && r.Length >= 12 && r != "N/A")
            ? r : $"TEMP{contador + 1:D4}";
    }
}
