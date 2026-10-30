using System.Text.RegularExpressions;

namespace CajaAhorro.Helpers;

/// <summary>
/// Utilidades para limpiar texto proveniente de los archivos de nómina
/// (SSZ / IMSS Bienestar), que suelen traer separadores y espacios sucios.
/// </summary>
public static class TextoNomina
{
    /// <summary>
    /// Quita los separadores que traen los nombres y normaliza espacios.
    /// SSZ usa el patrón "PATERNO,MATERNO/NOMBRE" — quita coma y diagonal.
    /// Ej.: "ALVARADO,ARTEAGA/BRENDA" → "ALVARADO ARTEAGA BRENDA".
    /// IMSS ya viene limpio, no le afecta.
    /// </summary>
    public static string LimpiarNombre(string? nombre)
    {
        if (string.IsNullOrWhiteSpace(nombre)) return string.Empty;

        // Diagonales y comas a espacio, para no pegar apellidos.
        var limpio = nombre.Replace('/', ' ').Replace('\\', ' ').Replace(',', ' ');

        // Colapsa espacios múltiples y recorta extremos.
        limpio = Regex.Replace(limpio, @"\s+", " ").Trim();

        return limpio;
    }
}