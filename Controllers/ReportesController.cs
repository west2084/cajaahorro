using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using CajaAhorro.Models;
using CajaAhorro.Services;

namespace CajaAhorro.Controllers;

[ApiController]
[Authorize]
[Route("api/reportes")]
public class ReportesController : ControllerBase
{
    private const string MimeXlsx =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private readonly ReporteService _reportes;
    private readonly ReportePrecargaBienestarService _bienestar;

    public ReportesController(ReporteService reportes, ReportePrecargaBienestarService bienestar)
    {
        _reportes = reportes;
        _bienestar = bienestar;
    }

    /// <summary>Reporte de créditos otorgados. Filtros opcionales: ?anio=2025&status=Activo</summary>
    [HttpGet("creditos")]
    public async Task<IActionResult> Creditos([FromQuery] int? anio, [FromQuery] string? status)
    {
        StatusPrestamo? statusEnum = null;
        if (!string.IsNullOrEmpty(status) &&
            Enum.TryParse<StatusPrestamo>(status, ignoreCase: true, out var parsed))
        {
            statusEnum = parsed;
        }

        var bytes = await _reportes.GenerarReporteCreditosAsync(anio, statusEnum);

        var nombre = $"Creditos_{(anio?.ToString() ?? "Todos")}" +
                     $"{(statusEnum.HasValue ? "_" + statusEnum : "")}" +
                     $"_{DateTime.Now:yyyyMMdd}.xlsx";

        return File(bytes, MimeXlsx, nombre);
    }

    /// <summary>Reporte de socios con datos faltantes (RFC, contacto, domicilio…).</summary>
    [HttpGet("socios-incompletos")]
    public async Task<IActionResult> SociosIncompletos()
    {
        var bytes = await _reportes.GenerarReporteSociosIncompletosAsync();
        var nombre = $"Socios_DatosFaltantes_{DateTime.Now:yyyyMMdd}.xlsx";
        return File(bytes, MimeXlsx, nombre);
    }

    /// <summary>Reporte de ahorradores. ?anio=2026&ciclo=Navideño (ciclo opcional)</summary>
    [HttpGet("ahorradores")]
    public async Task<IActionResult> Ahorradores([FromQuery] int? anio, [FromQuery] string? ciclo)
    {
        var anioFinal = anio ?? DateTime.Today.Year;

        CicloAhorro? cicloEnum = null;
        if (!string.IsNullOrEmpty(ciclo) &&
            Enum.TryParse<CicloAhorro>(ciclo, ignoreCase: true, out var parsed))
        {
            cicloEnum = parsed;
        }

        var bytes = await _reportes.GenerarReporteAhorradoresAsync(anioFinal, cicloEnum);

        var nombre = $"Ahorradores_{anioFinal}" +
                     $"{(cicloEnum.HasValue ? "_" + cicloEnum : "")}" +
                     $"_{DateTime.Now:yyyyMMdd}.xlsx";

        return File(bytes, MimeXlsx, nombre);
    }
    /// <summary>
    /// Descarga auditable de una precarga Bienestar y su conciliación.
    /// No representa importación contable. Incluye RFC/CURP; acceso administrativo.
    /// </summary>
    [HttpGet("bienestar-precarga/{cargaId:long}")]
    [Authorize(Roles = "Administrador")]
    public async Task<IActionResult> BienestarPrecarga(long cargaId)
    {
        if (cargaId <= 0) return BadRequest();
        try
        {
            var bytes = await _bienestar.ExportarExcelAsync(cargaId);
            Response.Headers["Cache-Control"] = "no-store";
            return File(bytes, MimeXlsx, $"Bienestar_Precarga_{cargaId}_{DateTime.Now:yyyyMMdd}.xlsx");
        }
        catch (KeyNotFoundException)
        {
            return NotFound("No existe la precarga solicitada.");
        }
    }

}
