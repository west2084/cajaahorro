using System.Globalization;
using System.Security.Cryptography;
using ClosedXML.Excel;
using CajaAhorro.Data;
using CajaAhorro.Models;
using Microsoft.EntityFrameworkCore;

namespace CajaAhorro.Services;

/// <summary>
/// Primera etapa: persiste precargas auditables en tablas independientes.
/// Nunca modifica Ahorros, Socios ni los saldos del sistema.
/// Habilitado solo en Development, hasta concluir pruebas y la fase contable.
/// </summary>
public sealed class AhorroBienestarGuardadoService
{
    private readonly AppDbContext _db;
    private readonly IHostEnvironment _env;
    public AhorroBienestarGuardadoService(AppDbContext db, IHostEnvironment env)
        => (_db, _env) = (db, env);

    public sealed record Resultado(long Id, int Ahorros, int Otros, int Incidencias, decimal Importe);

    public async Task<Resultado> GuardarPrecargaAsync(
        Stream archivo, string nombreArchivo, string usuario, CancellationToken ct = default)
    {
        if (!_env.IsDevelopment())
            throw new InvalidOperationException("La precarga Bienestar solo está habilitada en Development.");
        if (string.IsNullOrWhiteSpace(usuario))
            throw new InvalidOperationException("Se requiere usuario identificado para auditar la precarga.");
        if (string.IsNullOrWhiteSpace(nombreArchivo) || nombreArchivo.Length > 255)
            throw new InvalidDataException("Nombre de archivo inválido.");

        using var ms = new MemoryStream();
        await archivo.CopyToAsync(ms, ct);
        if (ms.Length is 0 or > (30 * 1024 * 1024))
            throw new InvalidDataException("El archivo debe tener entre 1 byte y 30 MB.");
        var bytes = ms.ToArray();
        var sha = Convert.ToHexString(SHA256.HashData(bytes));
        using var wb = new XLWorkbook(new MemoryStream(bytes));
        if (!AhorroBienestarDetalleReader.EsFormato(wb))
            throw new InvalidDataException("Encabezados de la primera hoja no corresponden al detalle IMSS Bienestar.");

        var hoja = wb.Worksheet(1);
        var carga = new AhorroBienestarCarga
        {
            NombreArchivo = nombreArchivo, Sha256 = sha, Hoja = hoja.Name,
            UsuarioCarga = usuario.Trim()[..Math.Min(usuario.Trim().Length, 150)],
            FechaCargaUtc = DateTime.UtcNow, Estado = "Precarga"
        };

        // RowsUsed(Contents) evita recorrer las filas vacías del rango Excel.
        foreach (var fila in hoja.RowsUsed(XLCellsUsedOptions.Contents))
        {
            int row = fila.RowNumber();
            if (row <= 1) continue;
            string rfc = Texto(row, 16).ToUpperInvariant();
            string concepto = Texto(row, 28).ToUpperInvariant();
            decimal? importe = Importe(row, 31);
            if (rfc.Length == 0 && concepto.Length == 0 && !importe.HasValue)
                continue;
            var fecha = Fecha(row, 15);
            bool periodoValido = int.TryParse(Texto(row, 11), out int qna) && qna is >= 1 and <= 24;
            string estatus = Texto(row, 33).ToUpperInvariant();
            bool esAhorro = concepto is "AAFA" or "AAFB";
            string? motivo = null;

            if (concepto is not ("AAFA" or "AAFB" or "AA39" or "AAFM"))
                motivo = "Concepto no reconocido";
            else if (rfc.Length is not (12 or 13) || !rfc.All(char.IsLetterOrDigit))
                motivo = "RFC inválido";
            else if (importe is null or <= 0)
                motivo = "Importe vacío o no positivo";
            else if (!periodoValido)
                motivo = "Quincena inválida";
            else if (fecha is null || fecha.Value.Year is < 2020 or > 2100)
                motivo = "Fecha de pago no reconocida";
            else if (estatus != "LIQUIDADO")
                motivo = "Estatus de pago requiere revisión: " + estatus;

            carga.Lineas.Add(new AhorroBienestarCargaLinea
            {
                FilaExcel = row, RFC = Cortar(rfc, 13),
                CURP = Texto(row, 17) is { Length: > 0 } curp ? Cortar(curp.ToUpperInvariant(), 18) : null,
                Nombre = Cortar(Texto(row, 21), 200),
                Plaza = Cortar(Texto(row, 22), 80),
                Concepto = Cortar(concepto, 10),
                Quincena = periodoValido ? qna : null,
                Anio = fecha?.Year, FechaPago = fecha,
                Importe = importe, EstatusPago = Cortar(estatus, 40),
                EstadoValidacion = motivo is not null ? "Incidencia" : esAhorro ? "AhorroValidado" : "OtroConcepto",
                Motivo = motivo
            });
            if (motivo is not null) carga.RegistrosConIncidencia++;
            else if (esAhorro)
            {
                carga.RegistrosValidosAhorro++;
                carga.ImporteAhorro += importe!.Value;
            }
            else carga.RegistrosOtrosConceptos++;
        }
        if (carga.Lineas.Count == 0)
            throw new InvalidDataException("No hay movimientos para registrar.");

        // El encabezado y todos sus renglones se guardan juntos o no se guarda ninguno.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        if (await _db.AhorroBienestarCargas.AnyAsync(x => x.Sha256 == sha, ct))
            throw new InvalidOperationException("El archivo ya fue registrado anteriormente.");
        _db.AhorroBienestarCargas.Add(carga);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new Resultado(carga.Id, carga.RegistrosValidosAhorro,
            carga.RegistrosOtrosConceptos, carga.RegistrosConIncidencia, carga.ImporteAhorro);

        string Texto(int r, int col) => hoja.Cell(r, col).GetValue<string>().Trim();
        decimal? Importe(int r, int col)
        {
            var celda = hoja.Cell(r, col);
            if (celda.IsEmpty()) return null;
            if (celda.Value.IsNumber) return (decimal)celda.Value.GetNumber();
            var s = celda.GetValue<string>().Trim();
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var x)) return x;
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out x)) return x;
            return null;
        }
        DateTime? Fecha(int r, int col)
        {
            var celda = hoja.Cell(r, col);
            if (celda.IsEmpty()) return null;
            if (celda.TryGetValue<DateTime>(out var d)) return d;
            if (celda.Value.IsNumber && celda.Value.GetNumber() is > 30000 and < 80000)
                return DateTime.FromOADate(celda.Value.GetNumber());
            return DateTime.TryParse(celda.GetValue<string>(), CultureInfo.GetCultureInfo("es-MX"),
                DateTimeStyles.None, out d) ? d : null;
        }
    }

    private static string Cortar(string valor, int max) => valor[..Math.Min(valor.Length, max)];
}
