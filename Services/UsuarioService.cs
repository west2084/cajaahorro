using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using CajaAhorro.Data;
using CajaAhorro.Models;

namespace CajaAhorro.Services;

public class UsuarioService
{
    private readonly AppDbContext _db;

    private const int Iteraciones = 100_000;
    private const int SaltBytes   = 16;
    private const int HashBytes   = 32;

    public UsuarioService(AppDbContext db) => _db = db;

    // ── Autenticación ────────────────────────────────────────────────────

    /// <summary>Valida credenciales. Regresa el usuario si son correctas, null si no.</summary>
    public async Task<Usuario?> ValidarCredencialesAsync(string nombreUsuario, string password)
    {
        var usuario = await _db.Usuarios
            .FirstOrDefaultAsync(u => u.NombreUsuario == nombreUsuario && u.Activo);

        if (usuario is null) return null;
        if (!VerificarPassword(password, usuario.PasswordHash)) return null;

        usuario.UltimoAcceso = DateTime.Now;
        await _db.SaveChangesAsync();
        return usuario;
    }

    // ── CRUD ─────────────────────────────────────────────────────────────

    public Task<List<Usuario>> ObtenerTodosAsync() =>
        _db.Usuarios.OrderBy(u => u.NombreUsuario).ToListAsync();

    public Task<bool> ExisteNombreUsuarioAsync(string nombreUsuario, int? excluirId = null) =>
        _db.Usuarios.AnyAsync(u => u.NombreUsuario == nombreUsuario
                                && (excluirId == null || u.Id != excluirId));

    public async Task<(bool Ok, string Mensaje)> CrearAsync(
        string nombreUsuario, string nombreCompleto, string password, RolUsuario rol)
    {
        nombreUsuario = nombreUsuario.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(nombreUsuario) || string.IsNullOrWhiteSpace(password))
            return (false, "Usuario y contraseña son obligatorios.");

        if (password.Length < 6)
            return (false, "La contraseña debe tener al menos 6 caracteres.");

        if (await ExisteNombreUsuarioAsync(nombreUsuario))
            return (false, $"El usuario '{nombreUsuario}' ya existe.");

        _db.Usuarios.Add(new Usuario
        {
            NombreUsuario  = nombreUsuario,
            NombreCompleto = nombreCompleto.Trim(),
            PasswordHash   = HashPassword(password),
            Rol            = rol,
            Activo         = true,
        });

        await _db.SaveChangesAsync();
        return (true, "Usuario creado correctamente.");
    }

    /// <summary>
    /// Edita nombre de usuario, nombre completo y rol. La contraseña se
    /// cambia aparte (CambiarPasswordAsync). Protege contra dejar el sistema
    /// sin administradores activos si se degrada al único admin a Operador.
    /// </summary>
    public async Task<(bool Ok, string Mensaje)> EditarDatosAsync(
        int usuarioId, string nombreUsuario, string nombreCompleto, RolUsuario rol)
    {
        nombreUsuario = (nombreUsuario ?? "").Trim().ToLowerInvariant();
        nombreCompleto = (nombreCompleto ?? "").Trim();

        if (string.IsNullOrWhiteSpace(nombreUsuario))
            return (false, "El nombre de usuario es obligatorio.");

        if (string.IsNullOrWhiteSpace(nombreCompleto))
            return (false, "El nombre completo es obligatorio.");

        var usuario = await _db.Usuarios.FindAsync(usuarioId);
        if (usuario is null) return (false, "Usuario no encontrado.");

        if (await ExisteNombreUsuarioAsync(nombreUsuario, usuarioId))
            return (false, $"El usuario '{nombreUsuario}' ya existe.");

        // No dejar el sistema sin administradores activos (mismo criterio
        // que al desactivar): si este es el único admin activo, no se puede
        // degradar a Operador.
        if (usuario.Rol == RolUsuario.Administrador && rol != RolUsuario.Administrador
            && usuario.Activo)
        {
            var otrosAdminsActivos = await _db.Usuarios.CountAsync(u =>
                u.Id != usuarioId && u.Rol == RolUsuario.Administrador && u.Activo);

            if (otrosAdminsActivos == 0)
                return (false, "No se puede quitar el rol de administrador al único administrador activo.");
        }

        usuario.NombreUsuario = nombreUsuario;
        usuario.NombreCompleto = nombreCompleto;
        usuario.Rol = rol;

        await _db.SaveChangesAsync();
        return (true, "Datos del usuario actualizados.");
    }

    public async Task<(bool Ok, string Mensaje)> CambiarPasswordAsync(int usuarioId, string nuevoPassword)
    {
        if (nuevoPassword.Length < 6)
            return (false, "La contraseña debe tener al menos 6 caracteres.");

        var usuario = await _db.Usuarios.FindAsync(usuarioId);
        if (usuario is null) return (false, "Usuario no encontrado.");

        usuario.PasswordHash = HashPassword(nuevoPassword);
        await _db.SaveChangesAsync();
        return (true, "Contraseña actualizada.");
    }

    /// <summary>Activa/desactiva. Protege contra dejar el sistema sin administradores activos.</summary>
    public async Task<(bool Ok, string Mensaje)> CambiarActivoAsync(int usuarioId, bool activo)
    {
        var usuario = await _db.Usuarios.FindAsync(usuarioId);
        if (usuario is null) return (false, "Usuario no encontrado.");

        if (!activo && usuario.Rol == RolUsuario.Administrador)
        {
            var otrosAdminsActivos = await _db.Usuarios.CountAsync(u =>
                u.Id != usuarioId && u.Rol == RolUsuario.Administrador && u.Activo);

            if (otrosAdminsActivos == 0)
                return (false, "No se puede desactivar al único administrador activo.");
        }

        usuario.Activo = activo;
        await _db.SaveChangesAsync();
        return (true, activo ? "Usuario activado." : "Usuario desactivado.");
    }

    // ── Seed ─────────────────────────────────────────────────────────────

    /// <summary>Crea el usuario admin inicial si la tabla está vacía.</summary>
    public async Task SeedAdminAsync()
    {
        if (await _db.Usuarios.AnyAsync()) return;

        _db.Usuarios.Add(new Usuario
        {
            NombreUsuario  = "admin",
            NombreCompleto = "Administrador del Sistema",
            PasswordHash   = HashPassword("CajaAhorro2026"),
            Rol            = RolUsuario.Administrador,
            Activo         = true,
        });
        await _db.SaveChangesAsync();
    }

    // ── Hashing PBKDF2 ───────────────────────────────────────────────────

    public static string HashPassword(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, Iteraciones, HashAlgorithmName.SHA256, HashBytes);

        return $"{Iteraciones}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool VerificarPassword(string password, string almacenado)
    {
        var partes = almacenado.Split('.');
        if (partes.Length != 3) return false;

        if (!int.TryParse(partes[0], out var iteraciones)) return false;

        var salt = Convert.FromBase64String(partes[1]);
        var hashGuardado = Convert.FromBase64String(partes[2]);

        var hashCalculado = Rfc2898DeriveBytes.Pbkdf2(
            password, salt, iteraciones, HashAlgorithmName.SHA256, hashGuardado.Length);

        return CryptographicOperations.FixedTimeEquals(hashCalculado, hashGuardado);
    }
}
