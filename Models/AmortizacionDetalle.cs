using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CajaAhorro.Models;

public class AmortizacionDetalle
{
    public int Id { get; set; }

    public int PrestamoId { get; set; }
    public Prestamo Prestamo { get; set; } = null!;

    // Número de quincena del pago (1, 2, 3... N)
    public int NumeroPago { get; set; }

    public int Quincena { get; set; }
    public int Anio { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal SaldoInicial { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Interes { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Capital { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Pago { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal SaldoFinal { get; set; }

    // Lo que realmente se aplicó según el archivo de retenciones
    [Column(TypeName = "decimal(18,2)")]
    public decimal? MontoAplicado { get; set; }

    public StatusPago StatusPago { get; set; } = StatusPago.Pendiente;

    // Observación libre del pago: útil sobre todo en aportaciones extra
    // (p. ej. "el trabajador aportó de más en ventanilla" o "se aplicó su
    // ahorro al crédito"). NULL en pagos normales.
    [MaxLength(300)]
    public string? Observaciones { get; set; }

    // Referencia al archivo de pagos quincenales que aplicó este pago
    public int? PagoQuincenalId { get; set; }
    public PagoQuincenalEncabezado? PagoQuincenal { get; set; }

    // Diferencia entre lo esperado y lo aplicado
    [Column(TypeName = "decimal(18,2)")]
    public decimal? Diferencia => MontoAplicado.HasValue
        ? MontoAplicado.Value - Pago
        : null;
}

public enum StatusPago
{
    Pendiente,
    Aplicado,
    Diferencia,   // Hubo discrepancia entre esperado y aplicado
    Suspendido    // El descuento no se aplicó ese quincena
}
