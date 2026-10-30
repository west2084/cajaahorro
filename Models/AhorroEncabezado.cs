using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CajaAhorro.Models;

/// <summary>
/// Encabezado de una importación de descuentos de ahorro.
/// Clave de unicidad: Quincena + Anio + Ciclo.
/// </summary>
public class AhorroEncabezado
{
    public int Id { get; set; }

    public int Quincena { get; set; }
    public int Anio     { get; set; }

    /// <summary>Ciclo al que pertenece: Navideño o Escolar.</summary>
    public CicloAhorro Ciclo { get; set; }

    [MaxLength(255)]
    public string NombreArchivo { get; set; } = string.Empty;

    [MaxLength(500)]
    public string TituloOriginal { get; set; } = string.Empty;

    public int TotalRegistros    { get; set; }
    public int SociosEncontrados { get; set; }
    public int SociosNoEncontrados { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ImporteTotal { get; set; }

    public int VecesImportado { get; set; } = 1;

    public DateTime  FechaCarga               { get; set; } = DateTime.Now;
    public DateTime? FechaUltimaActualizacion { get; set; }

    // Navegación
    public ICollection<Ahorro> Ahorros { get; set; } = new List<Ahorro>();
}
