using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CajaAhorro.Models;

/// <summary>
/// Parámetros administrativos del sistema. Es un registro ÚNICO (Id = 1):
/// el servicio crea la fila la primera vez y siempre actualiza la misma.
/// </summary>
public class ConfiguracionSistema
{
    public int Id { get; set; }

    /// <summary>Fecha en que se regresa a los socios el ahorro del ciclo ESCOLAR.</summary>
    public DateTime? FechaRetornoAhorroEscolar { get; set; }

    /// <summary>Fecha en que se regresa a los socios el ahorro del ciclo NAVIDEÑO.</summary>
    public DateTime? FechaRetornoAhorroNavideno { get; set; }

    /// <summary>
    /// Porcentaje de dividendos que se reparte sobre el ahorro.
    /// Se guarda como el número del porcentaje (ej. 8 = 8%), NO como fracción.
    /// </summary>
    [Column(TypeName = "decimal(7,4)")]
    public decimal PorcentajeDividendos { get; set; }

    public DateTime FechaActualizacion { get; set; } = DateTime.Now;
}
