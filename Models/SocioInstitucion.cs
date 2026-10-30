using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CajaAhorro.Models;

/// <summary>
/// Historial de institución de un socio.
/// Un socio puede migrar de Federal (IMSS Bienestar) a Estatal (SSZ) o viceversa.
/// El registro con FechaFin = null es la institución actual.
/// </summary>
public class SocioInstitucion
{
    public int Id { get; set; }

    public int SocioId { get; set; }
    public Socio Socio { get; set; } = null!;

    public InstitucionTipo Institucion { get; set; }

    // Quincena/año en que inició en esta institución
    public int QuincenaInicio { get; set; }
    public int AnioInicio     { get; set; }

    // Quincena/año en que dejó esta institución (null = sigue aquí actualmente)
    public int? QuincenaFin { get; set; }
    public int? AnioFin     { get; set; }

    [MaxLength(300)]
    public string? Observaciones { get; set; }

    public DateTime FechaRegistro { get; set; } = DateTime.Now;

    // ── Helper: ¿está activo en una quincena dada? ────────────────────────
    public bool EstaActivoEn(int quincena, int anio)
    {
        // Convertir a número comparable (ej: 202509)
        int qnaConsulta = anio * 100 + quincena;
        int qnaInicio   = AnioInicio * 100 + QuincenaInicio;

        if (qnaConsulta < qnaInicio) return false;

        if (AnioFin.HasValue && QuincenaFin.HasValue)
        {
            int qnaFin = AnioFin.Value * 100 + QuincenaFin.Value;
            return qnaConsulta <= qnaFin;
        }

        return true; // sin fecha fin = vigente
    }
}
