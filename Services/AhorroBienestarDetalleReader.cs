using ClosedXML.Excel;
using System.Globalization;

namespace CajaAhorro.Services;

/// <summary>
/// Lector de precarga de IMSS Bienestar: valida la estructura de la primera hoja,
/// sin depender de que se llame "Hoja1" o "Detalle1".
/// No guarda aportaciones en la base de datos.
/// </summary>
public static class AhorroBienestarDetalleReader
{
    private static readonly (int Columna, string Nombre)[] Encabezados =
    {
        (11, "Quincena"), (15, "Fecha Pago"), (16, "RFC"), (17, "CURP"),
        (21, "Nombre completo"), (22, "No. Plaza"), (28, "CtoPtda"),
        (30, "NombreCto"), (31, "Importe"), (33, "Estatus Pago")
    };

    public sealed class Resumen
    {
        public string Hoja { get; init; } = "";
        public string Concepto { get; init; } = "";
        public int Quincena { get; init; }
        public int Anio { get; init; }
        public int RegistrosValidos { get; set; }
        public int Incidencias { get; set; }
        public decimal Importe { get; set; }
        public DateTime? FechaPago { get; set; }
    }

    public sealed class Incidencia
    {
        public int Fila { get; init; }
        public string RFC { get; init; } = "";
        public string Concepto { get; init; } = "";
        public string Motivo { get; init; } = "";
    }

    public sealed class Analisis
    {
        public string Hoja { get; init; } = "";
        public List<Resumen> Resumenes { get; } = new();
        public List<Incidencia> Incidencias { get; } = new();
        public int FilasExaminadas { get; set; }
    }

    public static bool EsFormato(XLWorkbook libro)
    {
        var hoja = libro.Worksheet(1);
        return Encabezados.All(e => Normalizar(hoja.Cell(1, e.Columna).GetValue<string>())
                             == Normalizar(e.Nombre));
    }

    public static Analisis Analizar(XLWorkbook libro)
    {
        if (!EsFormato(libro))
            throw new InvalidDataException("La primera pestaña no coincide con los encabezados requeridos del detalle IMSS Bienestar.");

        var hoja = libro.Worksheet(1);
        var salida = new Analisis { Hoja = hoja.Name };
        var grupos = new Dictionary<(string Concepto, int Quincena, int Anio), Resumen>();

        // RowsUsed(Contents) evita recorrer el millón de filas vacías del rango
        // anunciado por algunas versiones del archivo de nómina.
        foreach (var fila in hoja.RowsUsed(XLCellsUsedOptions.Contents))
        {
            if (fila.RowNumber() <= 1) continue;
            var r = fila.RowNumber();
            string rfc = hoja.Cell(r, 16).GetValue<string>().Trim().ToUpperInvariant();
            string concepto = hoja.Cell(r, 28).GetValue<string>().Trim().ToUpperInvariant();
            string plaza = hoja.Cell(r, 22).GetValue<string>().Trim();
            string estatus = hoja.Cell(r, 33).GetValue<string>().Trim().ToUpperInvariant();
            var importe = ObtenerImporte(hoja.Cell(r, 31));
            var fecha = ObtenerFecha(hoja.Cell(r, 15));
            int.TryParse(hoja.Cell(r, 11).GetValue<string>().Trim(), out var quincena);
            int anio = fecha?.Year ?? 0;

            if (string.IsNullOrWhiteSpace(rfc) && string.IsNullOrWhiteSpace(concepto) &&
                !importe.HasValue) continue;

            salida.FilasExaminadas++;
            string? rechazo = null;
            if (!new[] { "AAFA", "AAFB", "AA39", "AAFM" }.Contains(concepto))
                rechazo = "Concepto no reconocido";
            else if (rfc.Length is not (12 or 13) || !rfc.All(char.IsLetterOrDigit))
                rechazo = "RFC inválido";
            else if (!importe.HasValue || importe <= 0)
                rechazo = "Importe vacío, negativo o no numérico";
            else if (quincena is < 1 or > 24)
                rechazo = "Quincena inválida";
            else if (anio is < 2020 or > 2100)
                rechazo = "Fecha de pago o año no reconocido";
            else if (!estatus.Equals("LIQUIDADO", StringComparison.OrdinalIgnoreCase))
                rechazo = $"Estatus de pago requiere revisión: {estatus}";

            if (rechazo != null)
            {
                salida.Incidencias.Add(new Incidencia { Fila = r, RFC = rfc, Concepto = concepto, Motivo = rechazo });
                continue;
            }

            var clave = (concepto, quincena, anio);
            if (!grupos.TryGetValue(clave, out var resumen))
            {
                resumen = new Resumen { Hoja = hoja.Name, Concepto = concepto,
                    Quincena = quincena, Anio = anio, FechaPago = fecha };
                grupos.Add(clave, resumen);
            }

            resumen.RegistrosValidos++;
            resumen.Importe += importe!.Value;
                // La plaza se conservará en la futura bitácora de importación.
            _ = plaza;
        }

        salida.Resumenes.AddRange(grupos.Values
            .OrderBy(x => x.Concepto).ThenBy(x => x.Anio).ThenBy(x => x.Quincena));
        return salida;
    }

    private static string Normalizar(string s)
        => new string(s.Trim().Where(c => !char.IsWhiteSpace(c) && c != '.').ToArray()).ToUpperInvariant();

    private static decimal? ObtenerImporte(IXLCell c)
    {
        if (c.IsEmpty()) return null;
        if (c.Value.IsNumber) return (decimal)c.Value.GetNumber();
        var s = c.GetValue<string>().Trim();
        if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)) return result;
        if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out result)) return result;
        return null;
    }

    private static DateTime? ObtenerFecha(IXLCell c)
    {
        if (c.IsEmpty()) return null;
        if (c.TryGetValue<DateTime>(out var fecha)) return fecha;
        if (c.Value.IsNumber)
        {
            var numero = c.Value.GetNumber();
            if (numero is > 30000 and < 80000) return DateTime.FromOADate(numero);
        }
        if (DateTime.TryParse(c.GetValue<string>(), CultureInfo.GetCultureInfo("es-MX"),
            DateTimeStyles.None, out var texto)) return texto;
        return null;
    }
}
