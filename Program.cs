using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using CajaAhorro.Data;
using CajaAhorro.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor(o => o.DetailedErrors = builder.Environment.IsDevelopment());
builder.Services.AddControllers();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Autenticación por cookies ────────────────────────────────────────────
builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath        = "/login";
        options.AccessDeniedPath = "/login";
        options.ExpireTimeSpan   = TimeSpan.FromHours(10);
        options.SlidingExpiration = true;
        options.Cookie.Name      = "CajaAhorro.Auth";
        options.Cookie.HttpOnly  = true;
    });

builder.Services.AddAuthorization();

// ── Servicios de la aplicación ───────────────────────────────────────────
builder.Services.AddScoped<PrestamoService>();
builder.Services.AddScoped<SocioService>();
builder.Services.AddScoped<CargaInicialService>();
builder.Services.AddScoped<ExpedienteService>();
builder.Services.AddScoped<RetencionService>();
builder.Services.AddScoped<RetencionImportService>();
builder.Services.AddScoped<AhorroImportService>();
builder.Services.AddScoped<UsuarioService>();
builder.Services.AddScoped<ReporteService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    // Crea el usuario admin inicial si la tabla Usuarios está vacía
    var usuarios = scope.ServiceProvider.GetRequiredService<UsuarioService>();
    await usuarios.SeedAdminAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();          // necesario para /login y /logout
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
