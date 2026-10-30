using Microsoft.EntityFrameworkCore;
using CajaAhorro.Data;
using CajaAhorro.Models;

namespace CajaAhorro.Services;

public class SocioService
{
    private readonly AppDbContext _db;

    public SocioService(AppDbContext db)
    {
        _db = db;
    }

    // Incluye Prestamos para poder contar en el listado
    public async Task<List<Socio>> ObtenerTodosAsync()
    {
        return await _db.Socios
            .Include(s => s.Prestamos)
            .Where(s => s.Activo)
            .OrderBy(s => s.Nombre)
            .ToListAsync();
    }

    // Incluye todo para el detalle y edición
    public async Task<Socio?> ObtenerPorIdAsync(int id)
    {
        return await _db.Socios
            .Include(s => s.Prestamos)
            .Include(s => s.Ahorros)
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<List<Socio>> BuscarAsync(string termino)
    {
        termino = termino.Trim().ToUpper();
        return await _db.Socios
            .Where(s => s.Activo &&
                (s.Nombre.ToUpper().Contains(termino) ||
                 s.RFC.ToUpper().Contains(termino) ||
                 (s.CURP != null && s.CURP.ToUpper().Contains(termino))))
            .OrderBy(s => s.Nombre)
            .Take(20)
            .ToListAsync();
    }

    public async Task<Socio> CrearAsync(Socio socio)
    {
        socio.Nombre = socio.Nombre.ToUpper().Trim();
        socio.RFC = socio.RFC.ToUpper().Trim();
        socio.CURP = string.IsNullOrWhiteSpace(socio.CURP) ? null : socio.CURP.ToUpper().Trim();
        socio.FechaRegistro = DateTime.Now;
        _db.Socios.Add(socio);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            _db.Entry(socio).State = EntityState.Detached;   // no envenenar el contexto
            throw;
        }
        return socio;
    }

    // Alta de socio que respeta los registros Provisionales creados por
    // importaciones: si el RFC ya existe pero es Provisional, lo COMPLETA
    // (no lo bloquea). Si ya existe y está Completo, es duplicado real.
    public async Task<ResultadoAlta> CrearOCompletarAsync(Socio nuevo)
    {
        nuevo.Nombre = (nuevo.Nombre ?? string.Empty).ToUpper().Trim();
        nuevo.RFC = (nuevo.RFC ?? string.Empty).ToUpper().Trim();
        nuevo.CURP = string.IsNullOrWhiteSpace(nuevo.CURP) ? null : nuevo.CURP.ToUpper().Trim();

        var existente = await _db.Socios.FirstOrDefaultAsync(s => s.RFC == nuevo.RFC);

        if (existente is null)
        {
            nuevo.FechaRegistro = DateTime.Now;
            nuevo.EstadoCaptura = EstadoCaptura.Completo;
            nuevo.Activo = true;
            _db.Socios.Add(nuevo);
            try
            {
                await _db.SaveChangesAsync();
            }
            catch
            {
                // CRÍTICO: si el INSERT falla (índice único, columna faltante,
                // etc.), la entidad queda rastreada en Added dentro del
                // DbContext, que en Blazor Server vive TODA la sesión del
                // navegador. Sin este detach, TODOS los guardados posteriores
                // de la sesión (socios, préstamos, lo que sea) vuelven a
                // intentar este INSERT roto y fallan "sin razón aparente".
                _db.Entry(nuevo).State = EntityState.Detached;
                throw;
            }
            return new ResultadoAlta(nuevo, TipoAlta.Creado);
        }

        if (existente.EstadoCaptura == EstadoCaptura.Provisional)
        {
            // Completar el provisional con los datos capturados a mano.
            existente.Nombre = nuevo.Nombre;
            existente.CURP = nuevo.CURP;
            existente.Adscripcion = nuevo.Adscripcion;
            existente.InstitucionActual = nuevo.InstitucionActual;
            existente.TipoNombramiento = nuevo.TipoNombramiento;
            existente.JornadaLaboral = nuevo.JornadaLaboral;
            existente.Calle = nuevo.Calle;
            existente.Numero = nuevo.Numero;
            existente.Colonia = nuevo.Colonia;
            existente.Municipio = nuevo.Municipio;
            existente.Localidad = nuevo.Localidad;
            existente.CodigoPostal = nuevo.CodigoPostal;
            existente.TelefonoFijo = nuevo.TelefonoFijo;
            existente.TelefonoCelular = nuevo.TelefonoCelular;
            existente.CorreoElectronico = nuevo.CorreoElectronico;
            existente.CantidadSolicitada = nuevo.CantidadSolicitada;
            existente.EstadoCaptura = EstadoCaptura.Completo;
            existente.Activo = true;
            try
            {
                await _db.SaveChangesAsync();
            }
            catch
            {
                await _db.Entry(existente).ReloadAsync();   // no envenenar el contexto
                throw;
            }
            return new ResultadoAlta(existente, TipoAlta.ProvisionalCompletado);
        }

        // Duplicado real (ya capturado).
        throw new SocioDuplicadoException(existente);
    }

    public async Task ActualizarAsync(Socio socio)
    {
        socio.Nombre = socio.Nombre.ToUpper().Trim();
        socio.RFC = socio.RFC.ToUpper().Trim();
        socio.CURP = string.IsNullOrWhiteSpace(socio.CURP) ? null : socio.CURP.ToUpper().Trim();
        _db.Socios.Update(socio);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch
        {
            await _db.Entry(socio).ReloadAsync();   // no envenenar el contexto
            throw;
        }
    }

    public async Task<bool> ExisteRFCAsync(string rfc, int? excluirId = null)
    {
        return await _db.Socios.AnyAsync(s =>
            s.RFC == rfc.ToUpper().Trim() &&
            (!excluirId.HasValue || s.Id != excluirId.Value));
    }

    public async Task DesactivarAsync(int id)
    {
        var socio = await _db.Socios.FindAsync(id);
        if (socio is null) return;
        socio.Activo = false;
        await _db.SaveChangesAsync();
    }
}

public enum TipoAlta
{
    Creado,
    ProvisionalCompletado
}

public record ResultadoAlta(CajaAhorro.Models.Socio Socio, TipoAlta Tipo);

public class SocioDuplicadoException : Exception
{
    public CajaAhorro.Models.Socio Existente { get; }
    public SocioDuplicadoException(CajaAhorro.Models.Socio existente)
        : base("Ya existe un socio capturado con este RFC.")
        => Existente = existente;
}
