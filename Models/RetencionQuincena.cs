using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CajaAhorro.Models;

/// <summary>
/// Encabezado de una carga de retenciones (una por archivo importado).
/// Clave de unicidad: Quincena + Anio + Fuente.
/// Agrupa todas las líneas del archivo para auditoría y cotejo.
/// </summary>
public class RetencionEncabezado
{
    public int Id { get; set; }

    public int Quincena { get; set; }
    public int Anio { get; set; }

    /// <summary>Estatal (SSZ) o Federal (IMSS Bienestar / OPD)</summary>
    public FuenteRetencion Fuente { get; set; }

    [MaxLength(500)]
    public string TituloOriginal { get; set; } = string.Empty;

    [MaxLength(255)]
    public string NombreArchivo { get; set; } = string.Empty;

    public int TotalRegistros { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal ImporteTotal { get; set; }

    /// <summary>Cuántos RFC del archivo coinciden con un socio de la caja.</summary>
    public int SociosEncontrados { get; set; }

    /// <summary>Cuántos RFC del archivo NO están registrados como socios.</summary>
    public int SociosNuevos { get; set; }

    public DateTime FechaCarga { get; set; } = DateTime.Now;
    public DateTime? FechaUltimaActualizacion { get; set; }
    public int VecesImportado { get; set; } = 1;

    // Navegación
    public ICollection<RetencionLinea> Lineas { get; set; } = new List<RetencionLinea>();
}

/// <summary>
/// Una línea del archivo de retenciones: un empleado en una quincena.
/// Solo guarda los datos del pago — los datos laborales van en Socio/Prestamo.
/// </summary>
public class RetencionLinea
{
    public int Id { get; set; }

    public int RetencionEncabezadoId { get; set; }
    public RetencionEncabezado Encabezado { get; set; } = null!;

    [MaxLength(13)]
    public string RFC { get; set; } = string.Empty;

    [MaxLength(200)]
    public string NombreEmpleado { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Importe { get; set; }

    /// <summary>Unidad o área de adscripción — útil para auditoría.</summary>
    [MaxLength(20)]
    public string UnidadResponsable { get; set; } = string.Empty;

    /// <summary>Concepto del descuento según el archivo (préstamo, ahorro, etc.).</summary>
    [MaxLength(100)]
    public string Concepto { get; set; } = string.Empty;

    /// <summary>True si el RFC coincide con un socio registrado en la caja.</summary>
    public bool EsSocio { get; set; } = false;

    /// <summary>Socio relacionado — null si no está registrado en la caja.</summary>
    public int? SocioId { get; set; }
    public Socio? Socio { get; set; }

    /// <summary>
    /// Indica si el socio migró de institución — es decir, aparece en este archivo
    /// pero su institución activa en esta quincena es la otra.
    /// </summary>
    public bool EsMigracion { get; set; } = false;

    // true  = el pago se aplicó a una amortización de un crédito activo.
    // false = entró el dinero pero NO hay crédito capturado al cual aplicarlo
    //         (socio provisional, o socio sin préstamo registrado).
    //         Estos son los que el auxiliar debe investigar y capturar.
    public bool AplicadoACredito { get; set; } = false;
}

public enum FuenteRetencion
{
    Estatal,  // Secretaría de Salud Zacatecas
    Federal   // OPD / IMSS Bienestar
}