using ClosedXML.Excel;
using CajaAhorro.Models;
using System.Text.RegularExpressions;

namespace CajaAhorro.Services;

/// <summary>
/// Reconocimiento de archivos IMSS Bienestar de cifras CAP con hojas AAFA / AAFB.
/// Solo lectura: no escribe a la base de datos. No importar otras hojas
/// (AA39 préstamos, AAFM mutualista, cuotas generales).
/// </summary>
public static class AhorroBienestarCifrasReader
{
    public sealed class Resumen
    {
        public string Hoja { get; init; } = "";
        public CicloAhorro Ciclo { get; init; }
        public int Quincena { get; init; }
        public int RegistrosValidos { get; init; }
        public int FilasNoValidas { get; init; }
        public decimal Importe { get; init; }
        public decimal? TotalDeclarado { get; init; }
        public bool TotalesCoinciden => TotalDeclarado.HasValue && Importe == TotalDeclarado.Value;
    }

    public static bool EsFormato(XLWorkbook libro)
        => libro.Worksheets.TryGetWorksheet("AAFA", out _)
        && libro.Worksheets.TryGetWorksheet("AAFB", out _);

    /// <summary>Devuelve cuatro resúmenes: escolar/navideño por cada quincena.
    /// Exige encabezados y compara con el pie TOTAL de cada hoja.</summary>
    public static List<Resumen> Analizar(XLWorkbook libro)
    {
        if (!EsFormato(libro))
            throw new InvalidDataException("El libro no contiene ambas hojas de ahorro AAFA y AAFB.");

        var resultado = new List<Resumen>();
        foreach (var (nombre, ciclo) in new[] {
            ("AAFB", CicloAhorro.Escolar), ("AAFA", CicloAhorro.Navideño)
        })
        {
            var hoja = libro.Worksheet(nombre);
            int filaCabecera = 0;
            int qna1 = 0, qna2 = 0;

            for (int fila = 1; fila <= Math.Min(15, hoja.LastRowUsed()?.RowNumber() ?? 0); fila++)
            {
                string nombreCol = hoja.Cell(fila, 3).GetValue<string>().Trim();
                if (!nombreCol.Equals("Nombre trabajador", StringComparison.OrdinalIgnoreCase))
                    continue;

                var m1 = Regex.Match(hoja.Cell(fila, 4).GetValue<string>(), @"QNA\s*0?(\d{1,2})", RegexOptions.IgnoreCase);
                var m2 = Regex.Match(hoja.Cell(fila, 5).GetValue<string>(), @"QNA\s*0?(\d{1,2})", RegexOptions.IgnoreCase);
                if (!m1.Success || !m2.Success) continue;
                qna1 = int.Parse(m1.Groups[1].Value);
                qna2 = int.Parse(m2.Groups[1].Value);
                filaCabecera = fila;
                break;
            }

            if (filaCabecera == 0 || qna1 < 1 || qna1 > 24 || qna2 < 1 || qna2 > 24 || qna1 == qna2)
                throw new InvalidDataException($"La hoja {nombre} no tiene encabezados válidos de QNA en columnas D y E.");

            var sumas = new decimal[2];
            var validos = new int[2];
            var invalidos = new int[2];
            decimal?[] declarados = new decimal?[2];
            bool footer = false;
            int ultima = hoja.LastRowUsed()?.RowNumber() ?? filaCabecera;
            for (int fila = filaCabecera + 1; fila <= ultima; fila++)
            {
                string rfc = hoja.Cell(fila, 2).GetValue<string>().Trim().ToUpperInvariant();
                string nombreTrabajador = hoja.Cell(fila, 3).GetValue<string>().Trim();
                if (nombreTrabajador.Equals("Total", StringComparison.OrdinalIgnoreCase))
                {
                    footer = true;
                    declarados[0] = LeerImporte(hoja.Cell(fila, 4));
                    declarados[1] = LeerImporte(hoja.Cell(fila, 5));
                    break;
                }
                if (string.IsNullOrWhiteSpace(rfc) && string.IsNullOrWhiteSpace(nombreTrabajador))
                    continue;
                for (int i = 0; i < 2; i++)
                {
                    decimal monto = LeerImporte(hoja.Cell(fila, 4 + i)) ?? 0;
                    if ((rfc.Length == 12 || rfc.Length == 13) && monto > 0)
                    {
                        validos[i]++;
                        sumas[i] += monto;
                    }
                    else if (monto != 0 || rfc.Length > 0)
                        invalidos[i]++;
                }
            }

            if (!footer)
                throw new InvalidDataException($"No se encontró fila Total en la hoja {nombre}.");

            for (int i = 0; i < 2; i++)
                resultado.Add(new Resumen {
                    Hoja = nombre,
                    Ciclo = ciclo,
                    Quincena = i == 0 ? qna1 : qna2,
                    RegistrosValidos = validos[i],
                    FilasNoValidas = invalidos[i],
                    Importe = sumas[i],
                    TotalDeclarado = declarados[i]
                });
        }
        return resultado;
    }

    private static decimal? LeerImporte(IXLCell celda)
    {
        if (celda.IsEmpty()) return null;
        if (celda.Value.IsNumber) return (decimal)celda.Value.GetNumber();
        var texto = celda.GetValue<string>().Trim();
        if (decimal.TryParse(texto, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.GetCultureInfo("es-MX"), out var valor)) return valor;
        if (decimal.TryParse(texto, System.Globalization.NumberStyles.Number,
            System.Globalization.CultureInfo.InvariantCulture, out valor)) return valor;
        return null;
    }
}
