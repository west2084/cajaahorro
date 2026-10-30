using System.ComponentModel.DataAnnotations;

namespace CajaAhorro.Models;

/// <summary>
/// Usuario del sistema (login). No confundir con Socio (miembro de la caja).
/// </summary>
public class Usuario
{
    public int Id { get; set; }

    [Required(ErrorMessage = "El nombre de usuario es obligatorio")]
    [MaxLength(50)]
    public string NombreUsuario { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre completo es obligatorio")]
    [MaxLength(200)]
    public string NombreCompleto { get; set; } = string.Empty;

    // Hash PBKDF2 en formato "iteraciones.saltBase64.hashBase64"
    [Required]
    [MaxLength(500)]
    public string PasswordHash { get; set; } = string.Empty;

    public RolUsuario Rol { get; set; } = RolUsuario.Operador;

    public bool Activo { get; set; } = true;

    public DateTime FechaRegistro { get; set; } = DateTime.Now;

    public DateTime? UltimoAcceso { get; set; }
}

public enum RolUsuario
{
    Administrador = 0,  // todo + gestión de usuarios
    Operador      = 1   // operación diaria (Sandra)
}
