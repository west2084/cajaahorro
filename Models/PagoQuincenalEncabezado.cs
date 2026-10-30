using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CajaAhorro.Models;

/// <summary>
/// Registro de cada archivo de pagos quincenales importado.
/// Clave de negocio: Quincena + Anio + NombreArchivo.
/// </summary>
public class PagoQuincenalEncabezado
{
    public int Id { get; set; }

    public int Quincena { get; set; }
    public int Anio     { get; set; }

    [MaxLength(255)]
    public string NombreArchivo { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Formato { get; set; } = string.Empty; // "IMSS BIENESTAR" | "TTIB"

    public int     TotalLineas       { get; set; }
    public int     CreditosAplicados { get; set; }
    public int     AhorrosAplicados  { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ImporteCreditos   { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ImporteAhorros    { get; set; }

    public int VecesImportado { get; set; } = 1;

    public DateTime  FechaCarga               { get; set; } = DateTime.Now;
    public DateTime? FechaUltimaActualizacion { get; set; }
}
