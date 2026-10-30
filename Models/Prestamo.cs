using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CajaAhorro.Models;

public class Prestamo
{
    public int Id { get; set; }

    // ── FOLIO ────────────────────────────────────────────────────────────────
    // Formato AAAAMM-NNNN (año+mes + consecutivo de 4 dígitos).
    // El consecutivo NNNN reinicia cada AÑO. Se asigna automáticamente al
    // crear el préstamo (PrestamoService.CrearAsync) y también en la carga
    // inicial (CargaInicialService, PrestamoService.GenerarFolioAsync).
    // Es NULL solo en préstamos que quedaron sin backfill de un import previo
    // a que existiera el folio (ver script CorregirFoliosPrestamos.sql).
    [MaxLength(20)]
    public string? Folio { get; set; }

    // ── FINIQUITO CON AHORRO ─────────────────────────────────────────────────
    // Intención registrada del socio: quiere que su ahorro se use para
    // terminar de pagar este crédito. Como el ahorro se acumula quincena a
    // quincena, esto es una PROMESA que se aplica cuando el ahorro alcanza el
    // monto solicitado (ver botón "Aplicar ahorro al finiquito" en el detalle
    // del préstamo, que lo convierte en un abono a capital).
    public bool UsarAhorroParaFiniquito { get; set; } = false;

    [Column(TypeName = "decimal(18,2)")]
    public decimal? MontoAhorroFiniquito { get; set; }

    // ── FONDO DE AHORRO APLICADO DESDE EL ALTA ───────────────────────────────
    // "Pago con Fondo de Ahorro" del formato oficial. Se captura al OTORGAR el
    // crédito (no después). NO cambia el total a pagar ni los intereses (que
    // se siguen calculando sobre el importe otorgado completo), pero SÍ reduce
    // el pago quincenal: el importe a distribuir entre las quincenas es
    // (TotalAPagar − AportacionAhorroInicial), porque ese fondo cubre esa parte
    // al final. Ej.: $49,500 total − $12,000 ahorro = $37,500 / 24 = $1,562.50.
    [Column(TypeName = "decimal(18,2)")]
    public decimal AportacionAhorroInicial { get; set; } = 0;

    [Required]
    public int SocioId { get; set; }
    public Socio Socio { get; set; } = null!;

    public TipoPrestamo TipoPrestamo { get; set; } = TipoPrestamo.Ordinario;

    [Required(ErrorMessage = "El importe es obligatorio")]
    [Column(TypeName = "decimal(18,2)")]
    public decimal ImporteSolicitado { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ImporteOtorgado { get; set; }

    // Tasa de interés quincenal (ej: 0.03 = 3%)
    [Column(TypeName = "decimal(6,4)")]
    public decimal TasaInteres { get; set; } = 0.03m;

    // Número de quincenas del plazo
    public int NumeroDescuentos { get; set; } = 24;

    [Column(TypeName = "decimal(18,2)")]
    public decimal PagoQuincenal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAPagar { get; set; }

    // Quincena y año de inicio (ej: Quincena 9, Año 2026)
    public int QuincenaInicial { get; set; }
    public int AnioInicial { get; set; }

    public int QuincenaFinal { get; set; }
    public int AnioFinal { get; set; }

    public DateTime FechaSolicitud { get; set; } = DateTime.Today;

    // Datos del cheque o transferencia
    public bool EsTransferencia { get; set; } = true;
    [MaxLength(50)]
    public string? NumeroChequeFolio { get; set; }
    public DateTime? FechaTransferencia { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? ImporteChequeFolio { get; set; }

    public StatusPrestamo Status { get; set; } = StatusPrestamo.Activo;

    [MaxLength(500)]
    public string? Observaciones { get; set; }

    public DateTime FechaRegistro { get; set; } = DateTime.Now;

    // ── REESTRUCTURA ─────────────────────────────────────────────────────────
    // Préstamo anterior que se reestructuró para originar éste (auto-referencia).
    public int? PrestamoAnteriorId { get; set; }
    public Prestamo? PrestamoAnterior { get; set; }

    // Capital insoluto del crédito anterior que se liquidó SIN intereses futuros
    // al reestructurar. Este monto se descuenta del importe otorgado: el socio
    // recibe en efectivo la diferencia (ImporteOtorgado − CapitalLiquidadoReestructura).
    // El interés del préstamo nuevo SÍ se calcula sobre el ImporteOtorgado completo.
    [Column(TypeName = "decimal(18,2)")]
    public decimal? CapitalLiquidadoReestructura { get; set; }

    // Navegación
    public ICollection<AmortizacionDetalle> Amortizacion { get; set; } = new List<AmortizacionDetalle>();

    // ── HELPERS (no se mapean a la base) ─────────────────────────────────────

    /// <summary>
    /// Efectivo que realmente se entrega al socio. En una reestructura es el
    /// importe otorgado menos el capital insoluto liquidado del crédito anterior;
    /// en un préstamo normal es el importe otorgado completo.
    /// </summary>
    [NotMapped]
    public decimal ImporteEntregadoNeto =>
        ImporteOtorgado - (CapitalLiquidadoReestructura ?? 0m);
}

public enum TipoPrestamo
{
    Ordinario,
    Reestructura
}

public enum StatusPrestamo
{
    Activo,
    Liquidado,
    Cancelado,
    Reestructurado
}
