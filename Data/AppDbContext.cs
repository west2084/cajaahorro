using Microsoft.EntityFrameworkCore;
using CajaAhorro.Models;

namespace CajaAhorro.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Socio>                   Socios                    => Set<Socio>();
    public DbSet<SocioInstitucion>        SocioInstituciones        => Set<SocioInstitucion>();
    public DbSet<Prestamo>                Prestamos                 => Set<Prestamo>();
    public DbSet<AmortizacionDetalle>     AmortizacionDetalles      => Set<AmortizacionDetalle>();
    public DbSet<Ahorro>                  Ahorros                   => Set<Ahorro>();
    public DbSet<AhorroEncabezado>        AhorroEncabezados         => Set<AhorroEncabezado>();
    public DbSet<RetencionEncabezado>     RetencionEncabezados      => Set<RetencionEncabezado>();
    public DbSet<RetencionLinea>          RetencionLineas           => Set<RetencionLinea>();
    public DbSet<PagoQuincenalEncabezado> PagoQuincenalEncabezados  => Set<PagoQuincenalEncabezado>();
    public DbSet<Usuario>                 Usuarios                  => Set<Usuario>();
    public DbSet<ConfiguracionSistema>    Configuraciones           => Set<ConfiguracionSistema>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Usuario (login del sistema) ──────────────────────────────────────
        modelBuilder.Entity<Usuario>(e =>
        {
            e.HasIndex(u => u.NombreUsuario).IsUnique();
        });

        // ── Socio ────────────────────────────────────────────────────────────
        modelBuilder.Entity<Socio>(e =>
        {
            e.HasIndex(s => s.RFC).IsUnique();
        });

        // ── SocioInstitucion → Socio ─────────────────────────────────────────
        // Historial de institución: un socio puede migrar entre Estatal y Federal.
        // Solo puede haber UN registro sin FechaFin (el vigente).
        modelBuilder.Entity<SocioInstitucion>(e =>
        {
            e.HasOne(si => si.Socio)
             .WithMany(s => s.Instituciones)
             .HasForeignKey(si => si.SocioId)
             .OnDelete(DeleteBehavior.Cascade);

            // Índice para búsqueda por socio y quincena
            e.HasIndex(si => new { si.SocioId, si.AnioInicio, si.QuincenaInicio });
        });

        // ── Prestamo → Socio ─────────────────────────────────────────────────
        modelBuilder.Entity<Prestamo>(e =>
        {
            e.HasOne(p => p.Socio)
             .WithMany(s => s.Prestamos)
             .HasForeignKey(p => p.SocioId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(p => new { p.SocioId, p.Status });

            // Folio único SOLO cuando no es null (préstamos viejos quedan en NULL).
            // Sin el filtro, SQL Server tomaría todos los NULL como duplicados.
            e.HasIndex(p => p.Folio)
             .IsUnique()
             .HasFilter("[Folio] IS NOT NULL");

            // Reestructura: auto-referencia al préstamo anterior.
            e.HasOne(p => p.PrestamoAnterior)
             .WithMany()
             .HasForeignKey(p => p.PrestamoAnteriorId)
             .OnDelete(DeleteBehavior.Restrict)
             .IsRequired(false);
        });

        // ── AmortizacionDetalle → Prestamo ───────────────────────────────────
        modelBuilder.Entity<AmortizacionDetalle>(e =>
        {
            e.HasOne(a => a.Prestamo)
             .WithMany(p => p.Amortizacion)
             .HasForeignKey(a => a.PrestamoId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(a => new { a.PrestamoId, a.NumeroPago }).IsUnique();
            e.Ignore(a => a.Diferencia);

            e.HasOne(a => a.PagoQuincenal)
             .WithMany()
             .HasForeignKey(a => a.PagoQuincenalId)
             .OnDelete(DeleteBehavior.SetNull)
             .IsRequired(false);
        });

        // ── AhorroEncabezado ─────────────────────────────────────────────────
        modelBuilder.Entity<AhorroEncabezado>(e =>
        {
            // Un archivo por ciclo por quincena (reimportación actualiza el mismo)
            e.HasIndex(a => new { a.Quincena, a.Anio, a.Ciclo }).IsUnique();
        });

        // ── Ahorro → Socio ───────────────────────────────────────────────────
        modelBuilder.Entity<Ahorro>(e =>
        {
            e.HasOne(a => a.Socio)
             .WithMany(s => s.Ahorros)
             .HasForeignKey(a => a.SocioId)
             .OnDelete(DeleteBehavior.Restrict);

            // Un registro por socio, ciclo y quincena
            e.HasIndex(a => new { a.SocioId, a.Quincena, a.Anio, a.Ciclo }).IsUnique();
            e.Ignore(a => a.Diferencia);

            e.HasOne(a => a.PagoQuincenal)
             .WithMany()
             .HasForeignKey(a => a.PagoQuincenalId)
             .OnDelete(DeleteBehavior.SetNull)
             .IsRequired(false);

            e.HasOne(a => a.AhorroEncabezado)
             .WithMany(enc => enc.Ahorros)
             .HasForeignKey(a => a.AhorroEncabezadoId)
             .OnDelete(DeleteBehavior.SetNull)
             .IsRequired(false);
        });

        // ── RetencionEncabezado ──────────────────────────────────────────────
        // Clave de negocio: Quincena + Anio + Fuente (un archivo por combo)
        modelBuilder.Entity<RetencionEncabezado>(e =>
        {
            e.HasIndex(r => new { r.Quincena, r.Anio, r.Fuente }).IsUnique();
        });

        // ── RetencionLinea → RetencionEncabezado ─────────────────────────────
        modelBuilder.Entity<RetencionLinea>(e =>
        {
            e.HasOne(l => l.Encabezado)
             .WithMany(h => h.Lineas)
             .HasForeignKey(l => l.RetencionEncabezadoId)
             .OnDelete(DeleteBehavior.Cascade);

            // Un RFC aparece una sola vez por archivo/quincena
            e.HasIndex(l => new { l.RetencionEncabezadoId, l.RFC }).IsUnique();

            e.HasOne(l => l.Socio)
             .WithMany()
             .HasForeignKey(l => l.SocioId)
             .OnDelete(DeleteBehavior.SetNull)
             .IsRequired(false);
        });
    }
}
