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

    public ReportesController(ReporteService reportes) => _reportes = reportes;

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
}
