using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using CajaAhorro.Services;

namespace CajaAhorro.Pages;

public class LoginModel : PageModel
{
    private readonly UsuarioService _usuarios;

    public LoginModel(UsuarioService usuarios) => _usuarios = usuarios;

    [BindProperty]
    public string NombreUsuario { get; set; } = string.Empty;

    [BindProperty]
    public string Password { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; set; }

    public IActionResult OnGet()
    {
        // Si ya está autenticado, mandarlo al dashboard
        if (User.Identity?.IsAuthenticated == true)
            return Redirect("/");

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var usuario = await _usuarios.ValidarCredencialesAsync(
            NombreUsuario.Trim().ToLowerInvariant(), Password);

        if (usuario is null)
        {
            Error = "Usuario o contraseña incorrectos.";
            return Page();
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new(ClaimTypes.Name,           usuario.NombreUsuario),
            new(ClaimTypes.GivenName,      usuario.NombreCompleto),
            new(ClaimTypes.Role,           usuario.Rol.ToString()),
        };

        var identity  = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc   = DateTimeOffset.UtcNow.AddHours(10),
            });

        // Solo aceptar rutas locales para evitar open redirect
        var destino = (!string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl))
            ? ReturnUrl
            : "/";

        return Redirect(destino);
    }
}
