using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CajaAhorro.Models;

/// <summary>
/// Registro de ahorro quincenal de un socio en un ciclo específico.
/// Los ciclos son:
///   NAVIDEÑO: enero a diciembre del mismo año (QNA 01/AAAA a QNA 24/AAAA).
///   ESCOLAR:  julio del año AAAA a junio del año AAAA+1
///             (QNA 13/AAAA a QNA 24/AAAA, luego QNA 01/AAAA+1 a QNA 12/AAAA+1).
/// El "año del ciclo escolar" siempre es el año en que EMPIEZA (julio).
/// </summary>
public class Ahorro
{
    public int Id { get; set; }

    public int SocioId { get; set; }
    public Socio Socio { get; set; } = null!;

    public int Quincena { get; set; }
    public int Anio     { get; set; }

    /// <summary>Ciclo al que pertenece este ahorro quincenal.</summary>
    public CicloAhorro Ciclo { get; set; } = CicloAhorro.Navideño;

    [Column(TypeName = "decimal(18,2)")]
    public decimal MontoRetenido { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? MontoEsperado { get; set; }

    public StatusAhorro Status { get; set; } = StatusAhorro.Aplicado;

    public DateTime FechaRegistro { get; set; } = DateTime.Now;

    // Referencia al encabezado de la importación de ahorro
    public int? AhorroEncabezadoId { get; set; }
    public AhorroEncabezado? AhorroEncabezado { get; set; }

    // Referencia al archivo de pagos quincenales (compatibilidad)
    public int? PagoQuincenalId { get; set; }
    public PagoQuincenalEncabezado? PagoQuincenal { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal? Diferencia => MontoEsperado.HasValue
        ? MontoRetenido - MontoEsperado.Value
        : null;
}

public enum CicloAhorro
{
    Navideño = 0,
    Escolar  = 1
}

public enum StatusAhorro
{
    Aplicado,
    Suspendido,
    Diferencia
}
