using CajaAhorro.Data;
using CajaAhorro.Models;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;

namespace CajaAhorro.Services;

/// <summary>
/// Conciliación y exportación de precargas: consulta informativa y auditable,
/// sin aplicar ni actualizar ahorros o socios. "Válido" no significa "aplicado".
/// </summary>
public sealed class ReportePrecargaBienestarService
{
    private readonly AppDbContext _db;
    public ReportePrecargaBienestarService(AppDbContext db) => _db = db;

    public sealed record FilaConciliada(AhorroBienestarCargaLinea Linea, string EstadoConciliacion, string Detalle);

    public sealed record Resumen(
        long CargaId, string Archivo, DateTime FechaUtc, string EstadoCarga,
        int AhorrosValidos, decimal ImporteAhorro,
        int OtrosConceptos, int Incidencias, int SociosNoEncontrados,
        int CoincidenciasAhorroExistente, int FilasSinObservaciones);

    public async Task<List<AhorroBienestarCarga>> HistorialAsync(CancellationToken ct = default)
        => await _db.AhorroBienestarCargas.AsNoTracking()
            .OrderByDescending(c => c.Id).Take(100).ToListAsync(ct);

    public async Task<(Resumen Resumen, List<FilaConciliada> Filas)> ConciliarAsync(
        long cargaId, CancellationToken ct = default)
    {
        var carga = await _db.AhorroBienestarCargas.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == cargaId, ct)
            ?? throw new KeyNotFoundException("La precarga solicitada no existe.");

        var lineas = await _db.AhorroBienestarCargaLineas.AsNoTracking()
            .Where(x => x.CargaId == cargaId).OrderBy(x => x.FilaExcel).ToListAsync(ct);

        var rfcs = lineas.Where(x => x.EstadoValidacion == "AhorroValidado")
            .Select(x => x.RFC).Distinct().ToList();
        var socios = await _db.Socios.AsNoTracking()
            .Where(x => rfcs.Contains(x.RFC))
            .Select(x => new { x.Id, x.RFC })
            .ToDictionaryAsync(x => x.RFC, x => x.Id, ct);

        // Consultar solo ahorros de los períodos contenidos en la precarga.
        // Los ahorros históricos no tienen campo de fuente/contrato:
        // una coincidencia exige revisión, NUNCA una actualización automática.
        var periodos = lineas.Where(x => x.EstadoValidacion == "AhorroValidado"
                    && x.Quincena.HasValue && x.Anio.HasValue)
            .Select(x => (Quincena: x.Quincena!.Value, Anio: x.Anio!.Value))
            .Distinct().ToList();
        var anios = periodos.Select(x => x.Anio).Distinct().ToList();
        var qnas = periodos.Select(x => x.Quincena).Distinct().ToList();
        var socioIds = socios.Values.ToList();
        var ahorros = await _db.Ahorros.AsNoTracking()
            .Where(x => anios.Contains(x.Anio) && qnas.Contains(x.Quincena)
                        && socioIds.Contains(x.SocioId))
            .Select(x => new { x.SocioId, x.Quincena, x.Anio, x.Ciclo })
            .ToListAsync(ct);
        var claves = ahorros.Select(x => (x.SocioId, x.Quincena, x.Anio, x.Ciclo)).ToHashSet();

        var salida = new List<FilaConciliada>(lineas.Count);
        int sinSocio = 0, existente = 0, sinObservaciones = 0;
        foreach (var l in lineas)
        {
            if (l.EstadoValidacion == "Incidencia")
            {
                salida.Add(new(l, "Rechazado en precarga", l.Motivo ?? "Validación fallida"));
                continue;
            }
            if (l.EstadoValidacion != "AhorroValidado")
            {
                salida.Add(new(l, "Otro concepto", "No se contabiliza como ahorro"));
                continue;
            }
            if (!socios.TryGetValue(l.RFC, out int socioId))
            {
                sinSocio++;
                salida.Add(new(l, "Socio no encontrado", "Registrar o validar socio antes de aplicar"));
                continue;
            }

            // El campo Anio del ahorro actual representa el año de la quincena,
            // aunque el ciclo escolar atraviesa dos años.
            var ciclo = l.Concepto == "AAFA" ? CicloAhorro.Navideño : CicloAhorro.Escolar;
            if (l.Quincena.HasValue && l.Anio.HasValue &&
                claves.Contains((socioId, l.Quincena.Value, l.Anio.Value, ciclo)))
            {
                existente++;
                salida.Add(new(l, "Ahorro existente", "Revisar institución y contrato; no sobrescribir"));
            }
            else
            {
                sinObservaciones++;
                salida.Add(new(l, "Sin coincidencia contable", "Revisión preliminar; no aplicado"));
            }
        }

        return (new Resumen(carga.Id, carga.NombreArchivo, carga.FechaCargaUtc, carga.Estado,
            carga.RegistrosValidosAhorro, carga.ImporteAhorro, carga.RegistrosOtrosConceptos,
            carga.RegistrosConIncidencia, sinSocio, existente, sinObservaciones), salida);
    }

    public async Task<byte[]> ExportarExcelAsync(long cargaId, CancellationToken ct = default)
    {
        var (res, filas) = await ConciliarAsync(cargaId, ct);
        using var libro = new XLWorkbook();

        var sum = libro.AddWorksheet("Resumen");
        var datos = new (string Etiqueta, string Valor)[]
        {
            ("Precarga", res.CargaId.ToString()),
            ("Archivo", res.Archivo),
            ("Estado", res.EstadoCarga + " - NO APLICADA A SALDOS"),
            ("Fecha carga (UTC)", res.FechaUtc.ToString("yyyy-MM-dd HH:mm:ss")),
            ("Movimientos de ahorro válidos", res.AhorrosValidos.ToString()),
            ("Importe de ahorro en precarga", res.ImporteAhorro.ToString("0.00")),
            ("Otros conceptos (no ahorro)", res.OtrosConceptos.ToString()),
            ("Rechazados por validación", res.Incidencias.ToString()),
            ("Socios no encontrados", res.SociosNoEncontrados.ToString()),
            ("Ahorros existentes (revisar)", res.CoincidenciasAhorroExistente.ToString()),
            ("Sin coincidencia contable", res.FilasSinObservaciones.ToString())
        };
        for (int i = 0; i < datos.Length; i++)
        {
            sum.Cell(i + 1, 1).Value = datos[i].Etiqueta;
            sum.Cell(i + 1, 2).Value = datos[i].Valor;
        }
        sum.Column(1).Width = 37;
        sum.Column(2).Width = 65;
        sum.Range(1, 1, 1, 2).Style.Font.SetBold();

        var det = libro.AddWorksheet("Detalle completo");
        var headers = new[] { "Fila Excel", "RFC", "Nombre", "CURP", "Plaza", "Concepto",
            "Quincena", "Año", "Fecha pago", "Importe", "Estatus pago",
            "Validación", "Conciliación", "Motivo/observación" };
        for (int col = 0; col < headers.Length; col++) det.Cell(1, col + 1).Value = headers[col];
        int row = 2;
        foreach (var x in filas)
        {
            var l = x.Linea;
            det.Cell(row, 1).Value = l.FilaExcel;
            det.Cell(row, 2).Value = l.RFC;
            det.Cell(row, 3).Value = l.Nombre;
            det.Cell(row, 4).Value = l.CURP ?? "";
            det.Cell(row, 5).Value = l.Plaza;
            det.Cell(row, 6).Value = l.Concepto;
            if (l.Quincena.HasValue) det.Cell(row, 7).Value = l.Quincena.Value;
            if (l.Anio.HasValue) det.Cell(row, 8).Value = l.Anio.Value;
            if (l.FechaPago.HasValue)
            {
                det.Cell(row, 9).Value = l.FechaPago.Value;
                det.Cell(row, 9).Style.DateFormat.Format = "dd/mm/yyyy";
            }
            if (l.Importe.HasValue) det.Cell(row, 10).Value = l.Importe.Value;
            det.Cell(row, 11).Value = l.EstatusPago;
            det.Cell(row, 12).Value = l.EstadoValidacion;
            det.Cell(row, 13).Value = x.EstadoConciliacion;
            det.Cell(row, 14).Value = x.Detalle;
            row++;
        }
        det.Range(2, 10, Math.Max(2, row - 1), 10).Style.NumberFormat.Format = "$ #,##0.00";
        det.SheetView.FreezeRows(1);
        det.Range(1, 1, Math.Max(1, row - 1), headers.Length).SetAutoFilter();
        det.Columns(1, headers.Length).AdjustToContents();
        for (int i = 1; i <= headers.Length; i++)
            if (det.Column(i).Width > 50) det.Column(i).Width = 50;
        det.Range(1, 1, 1, headers.Length).Style.Font.SetBold()
            .Fill.SetBackgroundColor(XLColor.FromHtml("#1d4ed8"))
            .Font.SetFontColor(XLColor.White);

        var rech = libro.AddWorksheet("Rechazados");
        foreach (var (h, i) in new[] { "Fila Excel", "RFC", "Concepto", "Importe", "Motivo" }
            .Select((h, i) => (h, i)))
            rech.Cell(1, i + 1).Value = h;
        int filaRech = 2;
        foreach (var l in filas.Where(x => x.Linea.EstadoValidacion == "Incidencia"))
        {
            rech.Cell(filaRech, 1).Value = l.Linea.FilaExcel;
            rech.Cell(filaRech, 2).Value = l.Linea.RFC;
            rech.Cell(filaRech, 3).Value = l.Linea.Concepto;
            if (l.Linea.Importe.HasValue) rech.Cell(filaRech, 4).Value = l.Linea.Importe.Value;
            rech.Cell(filaRech, 5).Value = l.Detalle;
            filaRech++;
        }
        rech.Columns(1, 5).AdjustToContents();
        rech.SheetView.FreezeRows(1);
        rech.Range(1, 1, 1, 5).Style.Font.SetBold();
        using var ms = new MemoryStream();
        libro.SaveAs(ms);
        return ms.ToArray();
    }
}
