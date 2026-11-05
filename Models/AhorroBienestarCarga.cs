using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CajaAhorro.Models;

/// <summary>
/// Bitácora de PRE-CARGA de IMSS Bienestar. No representa saldo ni aportación
/// aplicada; la tabla Ahorros sigue siendo la única fuente contable actual.
/// </summary>
public sealed class AhorroBienestarCarga
{
    public long Id { get; set; }
    [MaxLength(255)] public string NombreArchivo { get; set; } = "";
    [MaxLength(64)] public string Sha256 { get; set; } = "";
    [MaxLength(100)] public string Hoja { get; set; } = "";
    [MaxLength(150)] public string UsuarioCarga { get; set; } = "";
    public DateTime FechaCargaUtc { get; set; }
    [MaxLength(24)] public string Estado { get; set; } = "Precarga";
    public int RegistrosValidosAhorro { get; set; }
    public int RegistrosOtrosConceptos { get; set; }
    public int RegistrosConIncidencia { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal ImporteAhorro { get; set; }
    public ICollection<AhorroBienestarCargaLinea> Lineas { get; set; } = new List<AhorroBienestarCargaLinea>();
}

public sealed class AhorroBienestarCargaLinea
{
    public long Id { get; set; }
    public long CargaId { get; set; }
    public AhorroBienestarCarga Carga { get; set; } = null!;
    public int FilaExcel { get; set; }
    [MaxLength(13)] public string RFC { get; set; } = "";
    [MaxLength(18)] public string? CURP { get; set; }
    [MaxLength(200)] public string Nombre { get; set; } = "";
    [MaxLength(80)] public string Plaza { get; set; } = "";
    [MaxLength(10)] public string Concepto { get; set; } = "";
    public int? Quincena { get; set; }
    public int? Anio { get; set; }
    public DateTime? FechaPago { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? Importe { get; set; }
    [MaxLength(40)] public string EstatusPago { get; set; } = "";
    [MaxLength(24)] public string EstadoValidacion { get; set; } = "";
    [MaxLength(300)] public string? Motivo { get; set; }
}
