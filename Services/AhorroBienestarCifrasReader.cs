using ClosedXML.Excel;
using CajaAhorro.Models;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CajaAhorro.Services;

/// <summary>
/// Inspección sin escrituras del formato CIFRAS CAP de IMSS Bienestar.
/// B=RFC, C=Nombre trabajador, D/E=quincenas, F=total; el encabezado
/// de RFC de AAFA puede ser "." en lugar de "RFC".
/// </summary>
public static class AhorroBienestarCifrasReader
{
    public enum Concepto { AhorroNavideno, AhorroEscolar, Prestamos, FondoMutualista, NoImportar }

    private static readonly IReadOnlyDictionary<string, Concepto> ConceptosConocidos =
        new Dictionary<string, Concepto>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAFA"] = Concepto.AhorroNavideno,
            ["AAFB"] = Concepto.AhorroEscolar,
            ["AA39"] = Concepto.Prestamos,
            ["AAFM"] = Concepto.FondoMutualista
        };

    public sealed class Resumen
    {
        public string Hoja { get; init; } = "";
        public Concepto ConceptoSugerido { get; init; }
        public CicloAhorro? Ciclo => ConceptoSugerido switch
        {
            Concepto.AhorroNavideno => CicloAhorro.Navideño,
            Concepto.AhorroEscolar => CicloAhorro.Escolar,
            _ => null
        };
        public int Quincena { get; init; }
        public int RegistrosValidos { get; init; }
        public int FilasNoValidas { get; init; }
        public decimal Importe { get; init; }
    }

    public static bool EsFormato(XLWorkbook libro)
        => libro.Worksheets.TryGetWorksheet("AAFA", out _)
        && libro.Worksheets.TryGetWorksheet("AAFB", out _);

    public static bool EsConceptoConocido(string hoja, Concepto concepto)
        => ConceptosConocidos.TryGetValue(hoja, out var esperado) &&
           (concepto == esperado || concepto == Concepto.NoImportar);

    /// <summary>
    /// Retorna dos resúmenes por hoja identificada. Solo vista previa:
    /// nunca genera movimientos contables ni escribe a SQL.
    /// </summary>
    public static List<Resumen> Analizar(XLWorkbook libro)
    {
        if (!EsFormato(libro))
            throw new InvalidDataException("El libro no contiene las hojas de ahorro AAFA y AAFB.");

        var resultado = new List<Resumen>();
        foreach (var (nombre, concepto) in ConceptosConocidos)
        {
            if (!libro.Worksheets.TryGetWorksheet(nombre, out var hoja))
                continue;

            int filaCabecera = 0, qna1 = 0, qna2 = 0;
            var ultima = hoja.LastRowUsed()?.RowNumber() ?? 0;
            for (int fila = 1; fila <= Math.Min(15, ultima); fila++)
            {
                if (!hoja.Cell(fila, 3).GetValue<string>().Trim()
                    .Equals("Nombre trabajador", StringComparison.OrdinalIgnoreCase))
                    continue;
                var m1 = Regex.Match(hoja.Cell(fila, 4).GetValue<string>(), @"QNA\s*0?(\d{1,2})", RegexOptions.IgnoreCase);
                var m2 = Regex.Match(hoja.Cell(fila, 5).GetValue<string>(), @"QNA\s*0?(\d{1,2})", RegexOptions.IgnoreCase);
                if (!m1.Success || !m2.Success) continue;
                qna1 = int.Parse(m1.Groups[1].Value);
                qna2 = int.Parse(m2.Groups[1].Value);
                filaCabecera = fila;
                break;
            }

            if (filaCabecera == 0 || qna1 is < 1 or > 24 || qna2 is < 1 or > 24 || qna1 == qna2)
                throw new InvalidDataException($"La hoja {nombre} no tiene encabezados QNA válidos en D y E.");

            var sumas = new decimal[2];
            var validos = new int[2];
            var invalidos = new int[2];

            for (int fila = filaCabecera + 1; fila <= ultima; fila++)
            {
                string rfc = hoja.Cell(fila, 2).GetValue<string>().Trim().ToUpperInvariant();
                string trabajador = hoja.Cell(fila, 3).GetValue<string>().Trim();
                if (trabajador.Equals("Total", StringComparison.OrdinalIgnoreCase))
                {
                    // Los renglones TOTAL son informativos; no se usan para validar
                    // ni sumar importes. Cada QNA se calcula de RFC + importe.
                    break;
                }

                if (string.IsNullOrWhiteSpace(rfc) && string.IsNullOrWhiteSpace(trabajador))
                    continue;

                for (int indice = 0; indice < 2; indice++)
                {
                    decimal? importe = LeerImporte(hoja.Cell(fila, 4 + indice));
                    if ((rfc.Length == 12 || rfc.Length == 13) && importe > 0)
                    {
                        validos[indice]++;
                        sumas[indice] += importe.Value;
                    }
                    else if (importe != 0 || !string.IsNullOrWhiteSpace(rfc))
                        invalidos[indice]++;
                }
            }
            for (int indice = 0; indice < 2; indice++)
                resultado.Add(new Resumen
                {
                    Hoja = nombre,
                    ConceptoSugerido = concepto,
                    Quincena = indice == 0 ? qna1 : qna2,
                    RegistrosValidos = validos[indice],
                    FilasNoValidas = invalidos[indice],
                    Importe = sumas[indice]
                });
        }
        return resultado;
    }

    private static decimal? LeerImporte(IXLCell celda)
    {
        if (celda.IsEmpty()) return null;
        if (celda.Value.IsNumber) return (decimal)celda.Value.GetNumber();
        var texto = celda.GetValue<string>().Trim();
        if (decimal.TryParse(texto, NumberStyles.Number, CultureInfo.GetCultureInfo("es-MX"), out var valor))
            return valor;
        if (decimal.TryParse(texto, NumberStyles.Number, CultureInfo.InvariantCulture, out valor))
            return valor;
        return null;
    }
}
