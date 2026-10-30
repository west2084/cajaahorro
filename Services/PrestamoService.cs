using Microsoft.EntityFrameworkCore;
using CajaAhorro.Data;
using CajaAhorro.Models;

namespace CajaAhorro.Services;

public class PrestamoService
{
    private readonly AppDbContext _db;

    public PrestamoService(AppDbContext db)
    {
        _db = db;
    }

    // ── LISTADO ─────────────────────────────────────────────────────────────

    public async Task<List<Prestamo>> ObtenerTodosAsync()
    {
        return await _db.Prestamos
            .Include(p => p.Socio)
            .OrderByDescending(p => p.FechaSolicitud)
            .ToListAsync();
    }

    public async Task<List<Prestamo>> ObtenerActivosAsync()
    {
        return await _db.Prestamos
            .Include(p => p.Socio)
            .Where(p => p.Status == StatusPrestamo.Activo)
            .OrderByDescending(p => p.FechaSolicitud)
            .ToListAsync();
    }

    public async Task<Prestamo?> ObtenerPorIdAsync(int id)
    {
        return await _db.Prestamos
            .Include(p => p.Socio)
            .Include(p => p.Amortizacion.OrderBy(a => a.NumeroPago))
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    // ── CÁLCULO DE AMORTIZACIÓN ─────────────────────────────────────────────
    //
    // Modelo de INTERÉS FIJO TOTAL (no sobre saldo):
    //
    //   plazoCobro     = NumeroDescuentos + 2
    //                    (+2 porque el primer descuento empieza una quincena
    //                    después de otorgado, más una quincena de gracia final)
    //
    //   totalIntereses = ImporteOtorgado × TasaInteres × plazoCobro
    //   totalAPagar    = ImporteOtorgado + totalIntereses
    //   pagoQuincenal  = totalAPagar ÷ NumeroDescuentos  (fijo para todas las QNA)
    //
    // Ejemplo: $35,000 al 3% × 26 = $27,300 intereses → $62,300 total → $2,595.83/qna × 24
    public List<AmortizacionDetalle> CalcularAmortizacion(Prestamo prestamo)
    {
        var detalles = new List<AmortizacionDetalle>();
        decimal monto = prestamo.ImporteOtorgado;
        int pagos = prestamo.NumeroDescuentos;
        decimal tasa = prestamo.TasaInteres;     // ej. 0.03 = 3%

        // Quincenas base para calcular el interés (plazo + 2)
        int plazoCobro = pagos + 2;
        decimal totalIntereses = Math.Round(monto * tasa * plazoCobro, 2);
        decimal totalAPagar = monto + totalIntereses;

        // "Pago con Fondo de Ahorro": ese monto NO se cobra por quincena
        // (lo cubre el ahorro del socio al final), así que se descuenta del
        // importe a repartir. No afecta el total a pagar ni los intereses.
        decimal ahorroInicial = Math.Max(0, Math.Min(prestamo.AportacionAhorroInicial, totalAPagar));
        decimal montoADistribuir = totalAPagar - ahorroInicial;

        decimal pagoQna = Math.Round(montoADistribuir / pagos, 2);

        // El último pago absorbe el residuo de centavos por redondeo
        decimal sumaParciales = Math.Round(pagoQna * (pagos - 1), 2);
        decimal ultimoPago = Math.Round(montoADistribuir - sumaParciales, 2);

        prestamo.PagoQuincenal = pagoQna;
        prestamo.TotalAPagar = totalAPagar;

        // Interés plano. Cuando hay aportación de ahorro inicial, el importe
        // que se reparte por quincena es menor (montoADistribuir), y el
        // desglose interés/capital de cada quincena se hace en la MISMA
        // proporción que guardan en el total a pagar — así el formato oficial
        // cuadra al centavo (ej.: interés 615.53, capital 946.97, pago
        // 1,562.50) y la suma de intereses/capital cobrados baja en proporción
        // al ahorro aplicado. Sin ahorro, montoADistribuir == totalAPagar y
        // esto equivale al reparto normal (Σ interés = totalIntereses,
        // Σ capital = principal).
        decimal propInteres = totalAPagar > 0 ? totalIntereses / totalAPagar : 0m;
        decimal interesADistribuir = Math.Round(montoADistribuir * propInteres, 2);

        decimal interesQna = Math.Round(interesADistribuir / pagos, 2);
        decimal sumaInteresParciales = Math.Round(interesQna * (pagos - 1), 2);
        decimal ultimoInteres = Math.Round(interesADistribuir - sumaInteresParciales, 2);

        int quincena = prestamo.QuincenaInicial;
        int anio = prestamo.AnioInicial;
        decimal saldo = montoADistribuir;   // lo que de verdad se cobra por quincena

        for (int i = 1; i <= pagos; i++)
        {
            decimal pago = (i == pagos) ? ultimoPago : pagoQna;
            decimal interes = (i == pagos) ? ultimoInteres : interesQna;
            decimal capital = Math.Round(pago - interes, 2);
            decimal saldoFin = Math.Round(saldo - pago, 2);

            detalles.Add(new AmortizacionDetalle
            {
                PrestamoId = prestamo.Id,
                NumeroPago = i,
                Quincena = quincena,
                Anio = anio,
                SaldoInicial = saldo,
                Interes = interes,
                Capital = capital,
                Pago = pago,
                SaldoFinal = saldoFin,
                StatusPago = StatusPago.Pendiente
            });

            saldo = saldoFin;
            (quincena, anio) = SiguienteQuincena(quincena, anio);
        }

        var ultimo = detalles.Last();
        prestamo.QuincenaFinal = ultimo.Quincena;
        prestamo.AnioFinal = ultimo.Anio;

        return detalles;
    }

    /// <summary>
    /// Genera la amortización a partir de un pago quincenal FIJO ya conocido
    /// (p. ej. el capturado en el archivo de carga inicial), SIN recalcular el
    /// interés. Se usa cuando la tasa real difiere del 3% estándar —los
    /// ahorradores tienen préstamo al 2.5%— y la tabla debe respetar el pago
    /// que viene en el archivo en lugar del que saldría de capital × tasa.
    /// </summary>
    public List<AmortizacionDetalle> CalcularAmortizacionDesdePago(Prestamo prestamo, decimal pagoFijo)
    {
        var detalles = new List<AmortizacionDetalle>();
        int pagos = prestamo.NumeroDescuentos;
        pagoFijo = Math.Round(pagoFijo, 2);

        // El total se deriva del pago fijo (pago × n). No se recalcula interés.
        decimal totalAPagar = Math.Round(pagoFijo * pagos, 2);
        decimal sumaParciales = Math.Round(pagoFijo * (pagos - 1), 2);
        decimal ultimoPago = Math.Round(totalAPagar - sumaParciales, 2);  // absorbe residuo de centavos

        prestamo.PagoQuincenal = pagoFijo;
        prestamo.TotalAPagar = totalAPagar;

        // El interés total se deriva del propio pago capturado: total − capital.
        // No se recalcula con la tasa, así respeta el 2.5% del ahorrador o
        // cualquier tasa real del archivo. Si no hay capital (monto<=0), todo el
        // pago se trata como capital para no inventar intereses.
        decimal monto = prestamo.ImporteOtorgado;
        decimal totalIntereses = (monto > 0) ? Math.Round(totalAPagar - monto, 2) : 0m;
        decimal interesQna = Math.Round(totalIntereses / pagos, 2);
        decimal sumaInteresParciales = Math.Round(interesQna * (pagos - 1), 2);
        decimal ultimoInteres = Math.Round(totalIntereses - sumaInteresParciales, 2);

        int quincena = prestamo.QuincenaInicial;
        int anio = prestamo.AnioInicial;
        decimal saldo = totalAPagar;

        for (int i = 1; i <= pagos; i++)
        {
            decimal pago = (i == pagos) ? ultimoPago : pagoFijo;
            decimal interes = (i == pagos) ? ultimoInteres : interesQna;
            decimal capital = Math.Round(pago - interes, 2);
            decimal saldoFin = Math.Round(saldo - pago, 2);

            detalles.Add(new AmortizacionDetalle
            {
                PrestamoId = prestamo.Id,
                NumeroPago = i,
                Quincena = quincena,
                Anio = anio,
                SaldoInicial = saldo,
                Interes = interes,
                Capital = capital,
                Pago = pago,
                SaldoFinal = saldoFin,
                StatusPago = StatusPago.Pendiente
            });

            saldo = saldoFin;
            (quincena, anio) = SiguienteQuincena(quincena, anio);
        }

        var ultimo = detalles.Last();
        prestamo.QuincenaFinal = ultimo.Quincena;
        prestamo.AnioFinal = ultimo.Anio;

        return detalles;
    }

    /// <summary>
    /// Calcula el desglose plano interés/capital de un préstamo a partir de lo
    /// ya guardado, SIN tocar la base. Sirve para mostrar correctamente los
    /// préstamos creados antes de este cambio (que tienen Interes=0/Capital=pago).
    /// Devuelve un diccionario NumeroPago → (interés, capital).
    /// </summary>
    public static Dictionary<int, (decimal Interes, decimal Capital)>
        DesgloseInteresCapital(Prestamo prestamo)
    {
        var resultado = new Dictionary<int, (decimal, decimal)>();

        var detalles = prestamo.Amortizacion?.OrderBy(a => a.NumeroPago).ToList()
                       ?? new List<AmortizacionDetalle>();
        int pagos = detalles.Count;
        if (pagos == 0) return resultado;

        decimal totalAPagar = (prestamo.TotalAPagar > 0)
            ? prestamo.TotalAPagar
            : detalles.Sum(d => d.Pago);

        decimal monto = prestamo.ImporteOtorgado;
        decimal totalIntereses = (monto > 0)
            ? Math.Round(totalAPagar - monto, 2)
            : 0m;

        decimal interesQna = Math.Round(totalIntereses / pagos, 2);
        decimal sumaInteresParciales = Math.Round(interesQna * (pagos - 1), 2);
        decimal ultimoInteres = Math.Round(totalIntereses - sumaInteresParciales, 2);

        for (int i = 0; i < pagos; i++)
        {
            var d = detalles[i];
            bool esUltimo = (i == pagos - 1);
            decimal interes = esUltimo ? ultimoInteres : interesQna;
            decimal capital = Math.Round(d.Pago - interes, 2);
            resultado[d.NumeroPago] = (interes, capital);
        }

        return resultado;
    }

    // ── PRÉSTAMOS ACTIVOS DE UN SOCIO ───────────────────────────────────────

    /// <summary>Préstamos activos (no liquidados) de un socio, con su amortización.</summary>
    public async Task<List<Prestamo>> ObtenerActivosPorSocioAsync(int socioId)
    {
        return await _db.Prestamos
            .Include(p => p.Amortizacion)
            .Where(p => p.SocioId == socioId && p.Status == StatusPrestamo.Activo)
            .OrderByDescending(p => p.FechaSolicitud)
            .ToListAsync();
    }

    /// <summary>
    /// Saldo pendiente de un préstamo según el modelo de interés plano:
    /// suma de los pagos quincenales que aún no se han aplicado por completo
    /// (capital + intereses de las quincenas restantes).
    /// </summary>
    public static decimal SaldoPendiente(Prestamo p)
    {
        if (p.Amortizacion is null || !p.Amortizacion.Any())
            return p.TotalAPagar;

        return p.Amortizacion
            .Where(a => a.StatusPago != StatusPago.Aplicado)
            .Sum(a => a.Pago);
    }

    /// <summary>
    /// Saldo de CAPITAL pendiente (sin intereses) — lo que el socio en
    /// realidad todavía debe del monto prestado. Los intereses de las
    /// quincenas que aún no se cobran no son una deuda ya generada, así que
    /// NO se cuentan aquí. Esta es la cifra que se usa para reestructurar:
    /// el nuevo préstamo cubre el capital pendiente del anterior, no toda su
    /// deuda con intereses futuros incluidos.
    /// </summary>
    public static decimal SaldoCapitalPendiente(Prestamo p)
    {
        if (p.Amortizacion is null || !p.Amortizacion.Any())
            return p.ImporteOtorgado;

        return p.Amortizacion
            .Where(a => a.StatusPago != StatusPago.Aplicado)
            .Sum(a => a.Capital);
    }

    // ── GUARDAR ─────────────────────────────────────────────────────────────

    public enum ModoPrestamo
    {
        Nuevo,            // socio sin préstamos activos
        SegundoPrestamo,  // se conserva el anterior y se otorga otro en paralelo
        Reestructura      // se liquida el anterior y se otorga uno nuevo
    }

    public async Task<Prestamo> CrearAsync(
        Prestamo prestamo,
        ModoPrestamo modo = ModoPrestamo.Nuevo,
        int? prestamoAnteriorId = null)
    {
        // ── Candado contra doble envío ──────────────────────────────────────
        // Si la pantalla se queda "congelada" (problema de reconexión de
        // Blazor Server) el usuario a veces cree que no se guardó y vuelve a
        // capturar el mismo préstamo. Bajo Modo.Nuevo esto ya no puede pasar
        // (la pantalla exige elegir Reestructura/Segundo préstamo si ya hay
        // uno activo), pero un reintento accidental EN CUALQUIER modo, minutos
        // después, produce un préstamo idéntico. Si se detecta uno para el
        // mismo socio con los mismos datos capturado en los últimos 5 minutos,
        // se bloquea en vez de crear el duplicado.
        var haceCincoMin = DateTime.Now.AddMinutes(-5);
        var duplicadoReciente = await _db.Prestamos.AnyAsync(p =>
            p.SocioId == prestamo.SocioId &&
            p.ImporteOtorgado == prestamo.ImporteOtorgado &&
            p.ImporteSolicitado == prestamo.ImporteSolicitado &&
            p.NumeroDescuentos == prestamo.NumeroDescuentos &&
            p.TipoPrestamo == prestamo.TipoPrestamo &&
            p.FechaRegistro >= haceCincoMin);

        if (duplicadoReciente)
            throw new InvalidOperationException(
                "Ya se registró un préstamo idéntico para este socio hace unos momentos. " +
                "Si la pantalla pareció congelarse, es probable que SÍ se haya guardado — revisa " +
                "el expediente del socio antes de volver a capturarlo. Si de verdad necesitas otro " +
                "préstamo igual, espera unos minutos.");

        // Si es reestructura: liquidar/cerrar el préstamo anterior.
        if (modo == ModoPrestamo.Reestructura && prestamoAnteriorId.HasValue)
        {
            var anterior = await _db.Prestamos
                .Include(x => x.Amortizacion)
                .FirstOrDefaultAsync(x => x.Id == prestamoAnteriorId.Value);

            if (anterior is not null)
            {
                var saldoCapital = SaldoCapitalPendiente(anterior);
                var saldoTotal = SaldoPendiente(anterior);
                anterior.Status = StatusPrestamo.Reestructurado;
                anterior.Observaciones =
                    $"Reestructurado el {DateTime.Today:dd/MM/yyyy}. " +
                    $"Saldo de capital liquidado: {saldoCapital:C} " +
                    $"(deuda total con intereses futuros: {saldoTotal:C}). " +
                    (anterior.Observaciones ?? "");

                prestamo.TipoPrestamo = TipoPrestamo.Reestructura;
                prestamo.Observaciones =
                    $"Reestructura del préstamo #{anterior.Id} (saldo de capital {saldoCapital:C}). " +
                    (prestamo.Observaciones ?? "");
            }
        }
        else if (modo == ModoPrestamo.SegundoPrestamo)
        {
            prestamo.Observaciones =
                $"Segundo préstamo simultáneo. " + (prestamo.Observaciones ?? "");
        }

        // ── Folio ────────────────────────────────────────────────────────────
        // Formato AAAAMM-NNNN: año+mes de la fecha de solicitud + consecutivo
        // de 4 dígitos que reinicia cada AÑO (no cada mes). Los préstamos
        // importados de la carga inicial se quedan sin folio a propósito
        // (son historial previo a este sistema).
        prestamo.Folio = await GenerarFolioAsync(prestamo.FechaSolicitud);

        var amortizacion = CalcularAmortizacion(prestamo);

        _db.Prestamos.Add(prestamo);
        await _db.SaveChangesAsync();

        foreach (var detalle in amortizacion)
            detalle.PrestamoId = prestamo.Id;

        _db.AmortizacionDetalles.AddRange(amortizacion);
        await _db.SaveChangesAsync();

        return prestamo;
    }

    // ── CAPTURA MANUAL DE RETENCIONES ───────────────────────────────────────

    public class ResultadoPagoManual
    {
        public bool Aplicado { get; set; }
        public string Rfc { get; set; } = "";
        public string Nombre { get; set; } = "";
        public decimal Monto { get; set; }
        public string Mensaje { get; set; } = "";
        public int? PrestamoId { get; set; }
        public int Quincena { get; set; }
        public int Anio { get; set; }
    }

    /// <summary>
    /// Aplica manualmente un descuento quincenal a un socio.
    /// Busca la quincena exacta en la amortización de su préstamo activo;
    /// si no existe, toma el siguiente pago pendiente.
    /// </summary>
    public async Task<ResultadoPagoManual> RegistrarPagoManualAsync(
        int socioId, int quincena, int anio, decimal monto)
    {
        var res = new ResultadoPagoManual { Quincena = quincena, Anio = anio, Monto = monto };

        var socio = await _db.Socios.FindAsync(socioId);
        res.Rfc = socio?.RFC ?? "";
        res.Nombre = socio?.Nombre ?? "";

        // Buscar la quincena exacta en un préstamo activo
        var detalle = await _db.AmortizacionDetalles
            .Include(a => a.Prestamo)
            .Where(a => a.Prestamo.SocioId == socioId
                     && a.Prestamo.Status == StatusPrestamo.Activo
                     && a.Quincena == quincena
                     && a.Anio == anio)
            .OrderBy(a => a.PrestamoId)
            .FirstOrDefaultAsync();

        // Fallback: siguiente pago pendiente
        detalle ??= await _db.AmortizacionDetalles
            .Include(a => a.Prestamo)
            .Where(a => a.Prestamo.SocioId == socioId
                     && a.Prestamo.Status == StatusPrestamo.Activo
                     && a.StatusPago == StatusPago.Pendiente)
            .OrderBy(a => a.Anio).ThenBy(a => a.Quincena)
            .FirstOrDefaultAsync();

        if (detalle is null)
        {
            res.Aplicado = false;
            res.Mensaje = "Sin amortización pendiente para esta quincena.";
            return res;
        }

        detalle.MontoAplicado = monto;
        detalle.StatusPago = monto == 0
            ? StatusPago.Suspendido
            : Math.Abs(monto - detalle.Pago) <= 1m
                ? StatusPago.Aplicado
                : StatusPago.Diferencia;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            await _db.Entry(detalle).ReloadAsync();   // no envenenar el contexto
            throw;
        }

        res.Aplicado = true;
        res.PrestamoId = detalle.PrestamoId;
        res.Mensaje = detalle.StatusPago switch
        {
            StatusPago.Aplicado => $"Aplicado a QNA {detalle.Quincena:D2}/{detalle.Anio}.",
            StatusPago.Diferencia => $"Aplicado a QNA {detalle.Quincena:D2}/{detalle.Anio} con diferencia (esperado {detalle.Pago:C}).",
            StatusPago.Suspendido => $"Registrado como suspendido (monto 0) en QNA {detalle.Quincena:D2}/{detalle.Anio}.",
            _ => "Aplicado."
        };

        // Si la quincena capturada no existía en la amortización del préstamo
        // (p. ej. el préstamo empieza después, o esa quincena ya se aplicó) y
        // se usó el fallback del "siguiente pago pendiente", hay que decirlo
        // con claridad — de lo contrario el operador busca la quincena que
        // capturó en el préstamo y "no se refleja".
        if (detalle.Quincena != quincena || detalle.Anio != anio)
        {
            res.Mensaje = $"⚠ La QNA {quincena:D2}/{anio} no está pendiente en su préstamo; " +
                          $"se aplicó a la siguiente pendiente: QNA {detalle.Quincena:D2}/{detalle.Anio}.";
        }

        return res;
    }

    public async Task ActualizarStatusAsync(int id, StatusPrestamo status, string? observacion = null)
    {
        var prestamo = await _db.Prestamos.FindAsync(id);
        if (prestamo is null) return;

        prestamo.Status = status;
        if (observacion is not null)
            prestamo.Observaciones = observacion;

        await _db.SaveChangesAsync();
    }

    // ── CAMBIO MANUAL DE STATUS ─────────────────────────────────────────────

    /// <summary>
    /// Cambia manualmente el status de un préstamo dejando rastro de auditoría
    /// en Observaciones (fecha, status anterior → nuevo y motivo opcional).
    /// A diferencia de ActualizarStatusAsync, NO reemplaza las observaciones:
    /// antepone la nota conservando el historial previo.
    /// </summary>
    public async Task<bool> CambiarStatusManualAsync(
        int id, StatusPrestamo nuevoStatus, string? motivo = null)
    {
        var prestamo = await _db.Prestamos.FindAsync(id);
        if (prestamo is null) return false;

        var anterior = prestamo.Status;
        if (anterior == nuevoStatus && string.IsNullOrWhiteSpace(motivo))
            return true;   // nada que hacer

        prestamo.Status = nuevoStatus;

        var nota = $"[{DateTime.Now:dd/MM/yyyy HH:mm}] Status cambiado manualmente: " +
                   $"{anterior} → {nuevoStatus}." +
                   (string.IsNullOrWhiteSpace(motivo) ? "" : $" Motivo: {motivo.Trim()}.");

        prestamo.Observaciones = string.IsNullOrWhiteSpace(prestamo.Observaciones)
            ? nota
            : $"{nota} | {prestamo.Observaciones}";

        // La columna tiene MaxLength(500): truncar para no reventar el SaveChanges
        if (prestamo.Observaciones.Length > 500)
            prestamo.Observaciones = prestamo.Observaciones[..500];

        try
        {
            await _db.SaveChangesAsync();
            return true;
        }
        catch
        {
            // Evitar contexto envenenado: recargar la entidad a su estado en BD
            await _db.Entry(prestamo).ReloadAsync();
            throw;
        }
    }

    // ── EDICIÓN MANUAL DE UN PAGO DE LA AMORTIZACIÓN ────────────────────────

    /// <summary>
    /// Edita manualmente una quincena de la amortización: monto aplicado y
    /// status. Sirve para marcar como aplicado un pago pendiente, corregir un
    /// monto ya aplicado, o regresar un pago a Pendiente.
    /// Si el status queda en Pendiente se limpia el vínculo con el archivo de
    /// retenciones (PagoQuincenalId) para que una reimportación pueda volver a
    /// aplicarlo.
    /// </summary>
    // ── ABONO EXTRA A CAPITAL ────────────────────────────────────────────────
    // Ejemplo real: pago quincenal de $2,000; el trabajador deposita $4,000 al
    // banco → $2,000 cubren su quincena normal (llegan por la retención o
    // captura manual, como siempre) y $2,000 EXTRA van directo a capital.
    // El abono a capital reduce el capital pendiente, y la tabla de
    // amortización se recalcula según el modo que elija el operador:
    //   ReducirPagos:  el pago quincenal se mantiene; se ELIMINAN quincenas
    //                  del final (el préstamo termina antes). Los intereses de
    //                  las quincenas eliminadas ya no se cobran — coherente con
    //                  la regla de reestructura: interés futuro no es deuda.
    //   ReducirMonto:  se conservan las mismas quincenas; el capital restante
    //                  se reparte entre ellas y el pago quincenal baja. El
    //                  interés de cada quincena pendiente se conserva.

    public enum ModoAbonoCapital
    {
        ReducirPagos,   // menos quincenas, mismo pago
        ReducirMonto    // mismas quincenas, pago menor
    }

    public class SimulacionAbono
    {
        public bool EsValida { get; set; }
        public string? Error { get; set; }
        public bool LiquidaPrestamo { get; set; }
        public decimal Sobrante { get; set; }

        public decimal CapitalPendienteAntes { get; set; }
        public decimal CapitalPendienteDespues { get; set; }

        public int QuincenasPendientesAntes { get; set; }
        public int QuincenasPendientesDespues { get; set; }

        public decimal PagoQuincenalAntes { get; set; }
        public decimal PagoQuincenalDespues { get; set; }

        public string QuincenaFinalAntes { get; set; } = "";
        public string QuincenaFinalDespues { get; set; } = "";

        public decimal InteresQueSeDejaDeCobrar { get; set; }
    }

    /// <summary>Simula el efecto del abono SIN guardar nada — para que el
    /// operador vea los números antes de confirmar.</summary>
    public SimulacionAbono SimularAbonoCapital(Prestamo prestamo, decimal montoExtra, ModoAbonoCapital modo)
    {
        var sim = new SimulacionAbono();

        var pendientes = (prestamo.Amortizacion ?? new List<AmortizacionDetalle>())
            .Where(a => a.StatusPago == StatusPago.Pendiente)
            .OrderBy(a => a.NumeroPago)
            .ToList();

        if (montoExtra <= 0)
        { sim.Error = "El monto del abono debe ser mayor a cero."; return sim; }

        if (!pendientes.Any())
        { sim.Error = "Este préstamo no tiene quincenas pendientes."; return sim; }

        var capPendAntes = pendientes.Sum(a => a.Capital);
        sim.CapitalPendienteAntes = capPendAntes;
        sim.QuincenasPendientesAntes = pendientes.Count;
        sim.PagoQuincenalAntes = prestamo.PagoQuincenal;
        var ultAntes = pendientes.Last();
        sim.QuincenaFinalAntes = $"QNA {ultAntes.Quincena:D2}/{ultAntes.Anio}";

        var capPendDespues = capPendAntes - montoExtra;

        if (capPendDespues <= 0)
        {
            // El abono cubre TODO el capital pendiente → el préstamo se liquida.
            sim.EsValida = true;
            sim.LiquidaPrestamo = true;
            sim.Sobrante = -capPendDespues;
            sim.CapitalPendienteDespues = 0;
            sim.QuincenasPendientesDespues = 0;
            sim.PagoQuincenalDespues = 0;
            sim.QuincenaFinalDespues = "—";
            sim.InteresQueSeDejaDeCobrar = pendientes.Sum(a => a.Interes);
            return sim;
        }

        sim.CapitalPendienteDespues = capPendDespues;

        if (modo == ModoAbonoCapital.ReducirPagos)
        {
            var capXq = pendientes.First().Capital;
            var intXq = pendientes.First().Interes;
            if (capXq <= 0)
            { sim.Error = "La amortización no tiene desglose de capital — no se puede recalcular."; return sim; }

            int completas = (int)Math.Floor(capPendDespues / capXq);
            var residuo = Math.Round(capPendDespues - completas * capXq, 2);
            int nuevasFilas = completas + (residuo > 0 ? 1 : 0);

            sim.QuincenasPendientesDespues = nuevasFilas;
            sim.PagoQuincenalDespues = prestamo.PagoQuincenal;   // no cambia
            sim.InteresQueSeDejaDeCobrar = (pendientes.Count - nuevasFilas) * intXq;

            var ultNueva = pendientes[nuevasFilas - 1];
            sim.QuincenaFinalDespues = $"QNA {ultNueva.Quincena:D2}/{ultNueva.Anio}";
        }
        else
        {
            sim.QuincenasPendientesDespues = pendientes.Count;   // no cambia
            var capXfila = Math.Round(capPendDespues / pendientes.Count, 2);
            sim.PagoQuincenalDespues = capXfila + pendientes.First().Interes;
            sim.InteresQueSeDejaDeCobrar = 0;   // se conservan todas las quincenas
            sim.QuincenaFinalDespues = sim.QuincenaFinalAntes;
        }

        sim.EsValida = true;
        return sim;
    }

    /// <summary>
    /// Aplica un abono extra a capital y recalcula la tabla de amortización.
    /// Ver el comentario de la sección para la regla de cada modo.
    /// </summary>
    public async Task<SimulacionAbono> AplicarAbonoCapitalAsync(
        int prestamoId, decimal montoExtra, ModoAbonoCapital modo, string? observacion = null)
    {
        var prestamo = await _db.Prestamos
            .Include(p => p.Amortizacion)
            .FirstOrDefaultAsync(p => p.Id == prestamoId);

        if (prestamo is null)
            return new SimulacionAbono { Error = "Préstamo no encontrado." };

        var sim = SimularAbonoCapital(prestamo, montoExtra, modo);
        if (!sim.EsValida) return sim;

        var pendientes = prestamo.Amortizacion
            .Where(a => a.StatusPago == StatusPago.Pendiente)
            .OrderBy(a => a.NumeroPago)
            .ToList();

        try
        {
            if (sim.LiquidaPrestamo)
            {
                // Todas las pendientes se eliminan; el préstamo queda liquidado.
                _db.AmortizacionDetalles.RemoveRange(pendientes);
                prestamo.Status = StatusPrestamo.Liquidado;
            }
            else if (modo == ModoAbonoCapital.ReducirPagos)
            {
                var capXq = pendientes.First().Capital;
                var intXq = pendientes.First().Interes;
                var capRestante = sim.CapitalPendienteDespues;
                int nuevas = sim.QuincenasPendientesDespues;

                // Recalcular las primeras N filas pendientes; eliminar el resto.
                for (int i = 0; i < pendientes.Count; i++)
                {
                    if (i < nuevas)
                    {
                        var cap = Math.Min(capXq, capRestante);
                        pendientes[i].Capital = Math.Round(cap, 2);
                        pendientes[i].Interes = intXq;
                        pendientes[i].Pago = Math.Round(cap + intXq, 2);
                        capRestante -= cap;
                    }
                    else
                    {
                        _db.AmortizacionDetalles.Remove(pendientes[i]);
                    }
                }
            }
            else   // ReducirMonto
            {
                var capXfila = Math.Round(sim.CapitalPendienteDespues / pendientes.Count, 2);
                decimal asignado = 0;
                for (int i = 0; i < pendientes.Count; i++)
                {
                    // La última fila absorbe el residuo de centavos.
                    var cap = i == pendientes.Count - 1
                        ? Math.Round(sim.CapitalPendienteDespues - asignado, 2)
                        : capXfila;
                    asignado += cap;
                    pendientes[i].Capital = cap;
                    pendientes[i].Pago = Math.Round(cap + pendientes[i].Interes, 2);
                }
                prestamo.PagoQuincenal = sim.PagoQuincenalDespues;
            }

            // Reencadenar saldos de las filas pendientes que quedaron y
            // actualizar totales del préstamo.
            var vivas = prestamo.Amortizacion
                .Where(a => _db.Entry(a).State != EntityState.Deleted)
                .OrderBy(a => a.NumeroPago)
                .ToList();

            var pendientesVivas = vivas.Where(a => a.StatusPago == StatusPago.Pendiente).ToList();
            var saldo = pendientesVivas.Sum(a => a.Pago);
            foreach (var f in pendientesVivas)
            {
                f.SaldoInicial = Math.Round(saldo, 2);
                saldo -= f.Pago;
                f.SaldoFinal = Math.Round(saldo, 2);
            }

            prestamo.NumeroDescuentos = vivas.Count;
            prestamo.TotalAPagar = Math.Round(vivas.Sum(a => a.Pago), 2);
            if (vivas.Any())
            {
                var ult = vivas.Last();
                prestamo.QuincenaFinal = ult.Quincena;
                prestamo.AnioFinal = ult.Anio;
            }

            var notaModo = sim.LiquidaPrestamo
                ? "liquida el préstamo"
                : modo == ModoAbonoCapital.ReducirPagos
                    ? $"reduce plazo: {sim.QuincenasPendientesAntes}→{sim.QuincenasPendientesDespues} qnas pendientes"
                    : $"reduce pago: {sim.PagoQuincenalAntes:C}→{sim.PagoQuincenalDespues:C}";

            var nota = $"[{DateTime.Now:dd/MM/yyyy}] Abono a capital {montoExtra:C} ({notaModo})." +
                       (string.IsNullOrWhiteSpace(observacion) ? "" : $" Obs: {observacion.Trim()}.") +
                       (sim.Sobrante > 0 ? $" Sobrante a favor: {sim.Sobrante:C}." : "");

            prestamo.Observaciones = string.IsNullOrWhiteSpace(prestamo.Observaciones)
                ? nota
                : $"{nota} | {prestamo.Observaciones}";
            if (prestamo.Observaciones.Length > 500)
                prestamo.Observaciones = prestamo.Observaciones[..500];

            await _db.SaveChangesAsync();
            return sim;
        }
        catch
        {
            // No envenenar el contexto: descartar todo el recálculo fallido.
            foreach (var entry in _db.ChangeTracker.Entries()
                         .Where(e => e.State is EntityState.Modified or EntityState.Deleted)
                         .ToList())
            {
                await entry.ReloadAsync();
            }
            throw;
        }
    }

    /// <summary>Guarda la intención del socio de usar su ahorro para
    /// finiquitar este crédito (checkbox + monto en el detalle del préstamo).</summary>
    public async Task GuardarPreferenciaAhorroAsync(int prestamoId, bool usar, decimal? monto)
    {
        var prestamo = await _db.Prestamos.FindAsync(prestamoId);
        if (prestamo is null) return;

        prestamo.UsarAhorroParaFiniquito = usar;
        prestamo.MontoAhorroFiniquito = usar ? monto : null;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            await _db.Entry(prestamo).ReloadAsync();
            throw;
        }
    }

    public async Task<bool> EditarPagoAmortizacionAsync(
        int detalleId, decimal? montoAplicado, StatusPago status, string? observaciones = null)
    {
        var detalle = await _db.AmortizacionDetalles.FindAsync(detalleId);
        if (detalle is null) return false;

        if (status == StatusPago.Pendiente)
        {
            detalle.MontoAplicado = null;
            detalle.PagoQuincenalId = null;
        }
        else
        {
            detalle.MontoAplicado = montoAplicado.HasValue
                ? Math.Round(montoAplicado.Value, 2)
                : null;
        }

        detalle.StatusPago = status;

        // Observación libre — clave en aportaciones extra para dejar rastro
        // de POR QUÉ el monto difiere (aportó de más, ahorro aplicado, etc.).
        detalle.Observaciones = string.IsNullOrWhiteSpace(observaciones)
            ? null
            : observaciones.Trim();

        try
        {
            await _db.SaveChangesAsync();
            return true;
        }
        catch
        {
            await _db.Entry(detalle).ReloadAsync();
            throw;
        }
    }

    /// <summary>
    /// Sugiere el status según el monto aplicado, con la misma regla usada en
    /// la conciliación de retenciones: 0 = Suspendido, diferencia ≤ $1 =
    /// Aplicado, en otro caso Diferencia.
    /// </summary>
    public static StatusPago SugerirStatusPago(decimal montoAplicado, decimal pagoEsperado)
    {
        if (montoAplicado == 0) return StatusPago.Suspendido;
        return Math.Abs(montoAplicado - pagoEsperado) <= 1m
            ? StatusPago.Aplicado
            : StatusPago.Diferencia;
    }

    // ── CONCILIACIÓN DE RETENCIONES ─────────────────────────────────────────

    // Recibe los montos del archivo de retenciones de SS o OPD
    // y actualiza el status de cada quincena en la amortización
    public async Task ConciliarRetencionAsync(int prestamoId, int quincena, int anio, decimal montoAplicado)
    {
        var detalle = await _db.AmortizacionDetalles
            .FirstOrDefaultAsync(a => a.PrestamoId == prestamoId
                                   && a.Quincena == quincena
                                   && a.Anio == anio);

        if (detalle is null) return;

        detalle.MontoAplicado = montoAplicado;

        if (montoAplicado == 0)
            detalle.StatusPago = StatusPago.Suspendido;
        else if (Math.Abs(montoAplicado - detalle.Pago) <= 1m)
            detalle.StatusPago = StatusPago.Aplicado;
        else
            detalle.StatusPago = StatusPago.Diferencia;

        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// Genera el siguiente folio consecutivo con formato AAAAMM-NNNN: el
    /// año+mes de <paramref name="fecha"/>, y un consecutivo de 4 dígitos que
    /// reinicia cada AÑO (no cada mes) — es decir, sigue subiendo mes a mes
    /// dentro del mismo año, y vuelve a 0001 en enero del año siguiente.
    /// Público porque también lo usa CargaInicialService.
    /// </summary>
    public async Task<string> GenerarFolioAsync(DateTime fecha)
    {
        string anio = fecha.ToString("yyyy");
        string anioMes = fecha.ToString("yyyyMM");

        var foliosDelAnio = await _db.Prestamos
            .Where(p => p.Folio != null && p.Folio.StartsWith(anio))
            .Select(p => p.Folio!)
            .ToListAsync();

        int maxConsecutivo = 0;
        foreach (var f in foliosDelAnio)
        {
            var partes = f.Split('-');
            if (partes.Length == 2 && int.TryParse(partes[1], out int n) && n > maxConsecutivo)
                maxConsecutivo = n;
        }

        return $"{anioMes}-{(maxConsecutivo + 1):D4}";
    }

    // ── HELPERS ─────────────────────────────────────────────────────────────

    public static (int quincena, int anio) SiguienteQuincena(int quincena, int anio)
    {
        if (quincena == 24)
            return (1, anio + 1);
        return (quincena + 1, anio);
    }

    public static (int quincena, int anio) AnteriorQuincena(int quincena, int anio)
    {
        if (quincena == 1)
            return (24, anio - 1);
        return (quincena - 1, anio);
    }

    /// <summary>Quincena (1-24) y año a los que pertenece una fecha calendario:
    /// días 1-15 del mes = quincena impar, 16-fin de mes = quincena par.</summary>
    public static (int quincena, int anio) QuincenaDeFecha(DateTime fecha)
    {
        int q = (fecha.Month - 1) * 2 + (fecha.Day <= 15 ? 1 : 2);
        return (q, fecha.Year);
    }

    public static string FormatoQuincena(int quincena, int anio)
        => $"QNA {quincena:D2}/{anio}";

    // ── Tablero: préstamos otorgados y pagos reflejados por año ──────────────

    public class ResumenAnioPrestamos
    {
        public int Anio { get; set; }
        public int TotalPrestamos { get; set; }
        public decimal ImporteOtorgado { get; set; }

        public int Activos { get; set; }
        public int Liquidados { get; set; }
        public int Cancelados { get; set; }
        public int Reestructurados { get; set; }

        public int PagosEsperados { get; set; }
        public int PagosReflejados { get; set; }   // StatusPago != Pendiente
        public decimal PorcentajeAvance =>
            PagosEsperados > 0 ? Math.Round((decimal)PagosReflejados / PagosEsperados * 100, 1) : 0;
    }

    /// <summary>
    /// Resumen por año (de la fecha de solicitud): cuántos préstamos se
    /// otorgaron, su importe, y de todos los pagos programados en su tabla de
    /// amortización, cuántos ya se reflejaron (aplicados, con diferencia o
    /// suspendidos — cualquier cosa distinta de "Pendiente" significa que ya
    /// se procesó esa quincena, aunque no haya coincidido el monto exacto).
    /// <paramref name="filtroStatus"/> permite acotar a un solo status (p. ej.
    /// solo vigentes/Activos) — útil según se acumulan años de historial.
    /// </summary>
    public async Task<List<ResumenAnioPrestamos>> ObtenerResumenPorAnioAsync(StatusPrestamo? filtroStatus = null)
    {
        var qPrestamos = _db.Prestamos.AsQueryable();
        if (filtroStatus.HasValue) qPrestamos = qPrestamos.Where(p => p.Status == filtroStatus.Value);

        var porPrestamo = await qPrestamos
            .GroupBy(p => p.FechaSolicitud.Year)
            .Select(g => new
            {
                Anio = g.Key,
                Total = g.Count(),
                Importe = g.Sum(p => p.ImporteOtorgado),
                Activos = g.Count(p => p.Status == StatusPrestamo.Activo),
                Liquidados = g.Count(p => p.Status == StatusPrestamo.Liquidado),
                Cancelados = g.Count(p => p.Status == StatusPrestamo.Cancelado),
                Reestructurados = g.Count(p => p.Status == StatusPrestamo.Reestructurado),
            })
            .ToListAsync();

        var qPagos = _db.AmortizacionDetalles.AsQueryable();
        if (filtroStatus.HasValue) qPagos = qPagos.Where(a => a.Prestamo.Status == filtroStatus.Value);

        var porPago = await qPagos
            .GroupBy(a => a.Prestamo.FechaSolicitud.Year)
            .Select(g => new
            {
                Anio = g.Key,
                Esperados = g.Count(),
                Reflejados = g.Count(a => a.StatusPago != StatusPago.Pendiente),
            })
            .ToListAsync();

        var anios = porPrestamo.Select(p => p.Anio)
            .Union(porPago.Select(p => p.Anio))
            .OrderByDescending(a => a);

        var resultado = new List<ResumenAnioPrestamos>();
        foreach (var anio in anios)
        {
            var p = porPrestamo.FirstOrDefault(x => x.Anio == anio);
            var g = porPago.FirstOrDefault(x => x.Anio == anio);

            resultado.Add(new ResumenAnioPrestamos
            {
                Anio = anio,
                TotalPrestamos = p?.Total ?? 0,
                ImporteOtorgado = p?.Importe ?? 0,
                Activos = p?.Activos ?? 0,
                Liquidados = p?.Liquidados ?? 0,
                Cancelados = p?.Cancelados ?? 0,
                Reestructurados = p?.Reestructurados ?? 0,
                PagosEsperados = g?.Esperados ?? 0,
                PagosReflejados = g?.Reflejados ?? 0,
            });
        }

        return resultado;
    }

    /// <summary>Cuántos préstamos hay en total por cada status — para mostrar
    /// los contadores del filtro del tablero sin importar el año.</summary>
    public async Task<Dictionary<StatusPrestamo, int>> ContarPorStatusAsync()
    {
        var conteos = await _db.Prestamos
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Total = g.Count() })
            .ToListAsync();

        return conteos.ToDictionary(c => c.Status, c => c.Total);
    }
}