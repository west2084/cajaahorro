using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using CajaAhorro.Data;
using CajaAhorro.Services;

namespace CajaAhorro.Controllers;

[ApiController]
[Route("api/expediente")]
public class ExpedienteController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ExpedienteService _expediente;

    public ExpedienteController(AppDbContext db, ExpedienteService expediente)
    {
        _db = db;
        _expediente = expediente;
    }

    [HttpGet("{prestamoId:int}")]
    public async Task<IActionResult> Descargar(int prestamoId)
    {
        var prestamo = await _db.Prestamos
            .Include(p => p.Socio)
            .Include(p => p.Amortizacion.OrderBy(a => a.NumeroPago))
            .FirstOrDefaultAsync(p => p.Id == prestamoId);

        if (prestamo is null)
            return NotFound();

        var pdf = _expediente.GenerarExpedienteCompleto(prestamo, prestamo.Amortizacion.ToList());
        var nombre = $"Expediente_{prestamo.Socio.Nombre.Replace(" ", "_")}_{prestamo.Id}.pdf";

        return File(pdf, "application/pdf", nombre);
    }
}
