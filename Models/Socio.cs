using System.ComponentModel.DataAnnotations;

namespace CajaAhorro.Models;

public class Socio
{
    public int Id { get; set; }

    // ── DATOS PERSONALES ────────────────────────────────────────
    [Required(ErrorMessage = "El nombre es obligatorio")]
    [MaxLength(200)]
    public string Nombre { get; set; } = string.Empty;

    [Required(ErrorMessage = "El RFC es obligatorio")]
    [MaxLength(13)]
    public string RFC { get; set; } = string.Empty;

    // CURP: se usa en algunos archivos de carga (IMSS Bienestar) y en
    // trámites del socio. No es obligatoria porque los registros viejos
    // (carga inicial, importaciones) no siempre la traen.
    [MaxLength(18)]
    public string? CURP { get; set; }

    // ── ADSCRIPCIÓN Y NOMBRAMIENTO ──────────────────────────────
    [MaxLength(200)]
    public string Adscripcion { get; set; } = string.Empty;

    // Institución actual — ya NO es campo fijo.
    // Se obtiene del historial (SocioInstituciones donde FechaFin = null).
    // Se mantiene como campo de respaldo para compatibilidad con datos importados
    // hasta que se migre el historial completo.
    public InstitucionTipo InstitucionActual { get; set; } = InstitucionTipo.Estatal;

    // Federal, Homologado, Regularizado, Formalizado
    [MaxLength(50)]
    public string TipoNombramiento { get; set; } = string.Empty;

    // Matutina, Vespertina, Nocturna, Acumulada
    [MaxLength(30)]
    public string JornadaLaboral { get; set; } = string.Empty;

    // ── DOMICILIO ───────────────────────────────────────────────
    [MaxLength(200)]
    public string Calle { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Numero { get; set; } = string.Empty;

    [MaxLength(150)]
    public string Colonia { get; set; } = string.Empty;

    [MaxLength(150)]
    public string Municipio { get; set; } = string.Empty;

    [MaxLength(150)]
    public string Localidad { get; set; } = string.Empty;

    [MaxLength(10)]
    public string CodigoPostal { get; set; } = string.Empty;

    // ── CONTACTO ────────────────────────────────────────────────
    [MaxLength(15)]
    public string TelefonoFijo { get; set; } = string.Empty;

    [MaxLength(15)]
    public string TelefonoCelular { get; set; } = string.Empty;

    [MaxLength(150)]
    public string CorreoElectronico { get; set; } = string.Empty;

    // ── DATOS BANCARIOS ─────────────────────────────────────────
    // CLABE interbancaria a 18 dígitos para la dispersión de préstamos.
    // Se guarda como texto para conservar ceros a la izquierda.
    [MaxLength(18)]
    public string ClabeInterbancaria { get; set; } = string.Empty;

    // ── SOLICITUD ───────────────────────────────────────────────
    public decimal? CantidadSolicitada { get; set; }

    // ── STATUS ──────────────────────────────────────────────────
    public bool Activo { get; set; } = true;
    public DateTime FechaRegistro { get; set; } = DateTime.Now;

    // Estado del expediente:
    //   Completo    = capturado/validado a mano (alta normal, carga inicial).
    //   Provisional = creado automáticamente por una importación (ahorro/retención)
    //                 porque el RFC traía dinero pero el socio no estaba capturado.
    //                 Faltan datos por completar después.
    public EstadoCaptura EstadoCaptura { get; set; } = EstadoCaptura.Completo;

    // ── NAVEGACIÓN ──────────────────────────────────────────────
    public ICollection<Prestamo> Prestamos { get; set; } = new List<Prestamo>();
    public ICollection<Ahorro> Ahorros { get; set; } = new List<Ahorro>();
    public ICollection<SocioInstitucion> Instituciones { get; set; } = new List<SocioInstitucion>();

    // ── HELPERS ─────────────────────────────────────────────────

    /// <summary>
    /// Institución vigente en la quincena indicada.
    /// Si no hay historial cargado, regresa InstitucionActual como fallback.
    /// </summary>
    public InstitucionTipo InstitucionEn(int quincena, int anio)
    {
        var registro = Instituciones
            .FirstOrDefault(i => i.EstaActivoEn(quincena, anio));

        return registro?.Institucion ?? InstitucionActual;
    }

    /// <summary>Institución más reciente registrada en el historial.</summary>
    public InstitucionTipo InstitucionVigente()
    {
        var ultimo = Instituciones
            .Where(i => !i.AnioFin.HasValue)
            .OrderByDescending(i => i.AnioInicio * 100 + i.QuincenaInicio)
            .FirstOrDefault();

        return ultimo?.Institucion ?? InstitucionActual;
    }
}

public enum InstitucionTipo
{
    Estatal,   // Secretaría de Salud Zacatecas (SSZ)
    Federal    // OPD / IMSS Bienestar
}

public enum EstadoCaptura
{
    Completo = 0,   // valor por defecto: altas manuales y carga inicial
    Provisional = 1    // creado por importación, faltan datos
}
