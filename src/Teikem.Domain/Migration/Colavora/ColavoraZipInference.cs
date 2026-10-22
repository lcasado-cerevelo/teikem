using System.Globalization;
using System.Text;

namespace Teikem.Domain.Migration.Colavora;

/// <summary>
/// Un envío de Colavora puede venir sin ZIP del consignatario (2026-10-22: ~1.070 desde el 1 de octubre, de clientes que
/// mandan la orden sin código postal). Reglas puras para inferirlo (la zona de despacho sale del ZIP):
/// 1) historial del mismo consignatario en Colavora (nombre + dirección, y si no hay, solo el nombre) cuando todos sus envíos
///    anteriores con ZIP traen el mismo; 2) el municipio que nombra el texto (nombre del consignatario o dirección), solo si
///    todos los ZIP de ese municipio caen en UNA sola zona. Nada más se adivina: lo demás queda sin zona.
/// </summary>
public static class ColavoraZipInference
{
    public const string SourceHistory = "historial";
    public const string SourceMunicipality = "municipio";

    /// <summary>Lo inferido: ZIP (si es uno solo) y ciudad para la parada, la zona (0 = sin zona) y de dónde salió.</summary>
    public sealed record Inferred(string? Zip, string? City, int ZoneId, string Source, string Detail);

    /// <summary>Texto para comparar: sin acentos, mayúsculas, todo lo que no es letra ni número es un espacio ('Urb. Bayamón' → 'URB BAYAMON').</summary>
    public static string Fold(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormD))
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(ch) ? char.ToUpperInvariant(ch) : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Palabras que, justo antes del nombre de un pueblo, lo vuelven nombre de calle ('AVE CAROLINA'), no el pueblo del consignatario.</summary>
    private static readonly HashSet<string> StreetWords = new(StringComparer.Ordinal) { "AVE", "AVENIDA", "CALLE", "C", "CALLEJON", "CARR", "CARRETERA", "PASEO", "CAMINO", "PLAZA" };

    /// <summary>Pueblos (y barrios con ZIP propio) de la tabla Zip de Colavora, con los ZIP de cada uno.</summary>
    public sealed class MunicipalityIndex
    {
        private readonly List<(string Folded, string Display, IReadOnlyList<string> Zips)> _items;
        private readonly IReadOnlyDictionary<string, int> _zoneIdByZip;

        /// <param name="cityByZip">ZIP → ciudad tal como viene en Colavora ('LA PLATA, AIBONITO': el pueblo es lo que sigue a la última coma).</param>
        /// <param name="zoneIdByZip">ZIP → zona de despacho de Teikem (0 o ausente = sin zona; negativo = zona de una simulación, aún sin id real).</param>
        public MunicipalityIndex(IEnumerable<KeyValuePair<string, string>> cityByZip, IReadOnlyDictionary<string, int> zoneIdByZip)
        {
            _zoneIdByZip = zoneIdByZip;
            _items = cityByZip
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
                .GroupBy(kv => Fold(Town(kv.Value)))
                .Where(g => g.Key.Length >= 4)
                .Select(g => (g.Key, Town(g.First().Value), (IReadOnlyList<string>)g.Select(kv => kv.Key).Distinct(StringComparer.Ordinal).OrderBy(z => z, StringComparer.Ordinal).ToList()))
                .OrderByDescending(x => x.Key.Length)
                .ToList();
        }

        public int Count => _items.Count;

        private static string Town(string city)
        {
            var i = city.LastIndexOf(',');
            return (i >= 0 ? city[(i + 1)..] : city).Trim();
        }

        /// <summary>
        /// Pueblo(s) que nombra el texto y su zona. Resuelto solo si todos los ZIP de lo que se encontró caen en UNA zona; si el texto
        /// nombra pueblos de zonas distintas, o el pueblo se reparte entre zonas, devuelve null.
        /// </summary>
        public Inferred? Find(params string?[] texts)
        {
            var padded = " " + Fold(string.Join(' ', texts.Where(t => !string.IsNullOrWhiteSpace(t)))) + " ";
            if (padded.Length <= 2) return null;
            var found = new List<(string Display, IReadOnlyList<string> Zips)>();
            foreach (var (folded, display, zips) in _items)
            {
                var needle = " " + folded + " ";
                var at = 0;
                var hit = false;
                while ((at = padded.IndexOf(needle, at, StringComparison.Ordinal)) >= 0)
                {
                    var before = padded[..at].TrimEnd().Split(' ').LastOrDefault() ?? string.Empty;
                    if (!StreetWords.Contains(before)) { hit = true; break; }
                    at += needle.Length - 1;
                }
                // un pueblo contenido en otro ya encontrado ('SAN JUAN' dentro de 'SAN JUAN BAUTISTA') no suma
                if (hit && !found.Any(f => Fold(f.Display).Contains(folded, StringComparison.Ordinal))) found.Add((display, zips));
            }
            if (found.Count == 0) return null;
            var zoneIds = found.SelectMany(f => f.Zips).Select(z => _zoneIdByZip.GetValueOrDefault(z)).Where(id => id != 0).Distinct().ToList();
            if (zoneIds.Count != 1) return null;
            var allZips = found.SelectMany(f => f.Zips).Distinct(StringComparer.Ordinal).ToList();
            var only = found.Count == 1 && allZips.Count == 1 ? allZips[0] : null;
            return new Inferred(only, found.Count == 1 ? found[0].Display : null, zoneIds[0], SourceMunicipality,
                $"municipio {string.Join(" / ", found.Select(f => f.Display))} del texto de la dirección");
        }
    }

    /// <summary>
    /// ZIP del historial del consignatario: <paramref name="byNameAndAddress"/> son los ZIP distintos de sus envíos anteriores con el mismo
    /// nombre y dirección; <paramref name="byName"/>, los del mismo nombre. Se usa el primer grupo que tenga ZIP y solo si es uno.
    /// Con varios ZIP distintos no se adivina (se pasa al municipio).
    /// </summary>
    public static string? FromHistory(IReadOnlyCollection<string> byNameAndAddress, IReadOnlyCollection<string> byName)
    {
        if (byNameAndAddress.Count == 1) return byNameAndAddress.First();
        if (byNameAndAddress.Count == 0 && byName.Count == 1) return byName.First();
        return null;
    }

    /// <summary>La inferencia completa de un consignatario sin ZIP: historial primero, municipio después.</summary>
    public static Inferred? Infer(string? consigneeName, string? line1, string? line2,
        IReadOnlyCollection<string> historyByNameAndAddress, IReadOnlyCollection<string> historyByName,
        MunicipalityIndex? municipalities, IReadOnlyDictionary<string, int> zoneIdByZip, IReadOnlyDictionary<string, string> cityByZip)
    {
        if (FromHistory(historyByNameAndAddress, historyByName) is { } zip)
        {
            var zone = zoneIdByZip.GetValueOrDefault(zip);
            if (zone != 0)   // en la simulación las zonas aún no existen: llevan un id negativo provisional
                return new Inferred(zip, cityByZip.GetValueOrDefault(zip), zone, SourceHistory,
                    historyByNameAndAddress.Count == 1 ? $"ZIP {zip} de sus envíos anteriores (mismo nombre y dirección)" : $"ZIP {zip} de sus envíos anteriores (mismo nombre)");
        }
        var byTown = municipalities?.Find(consigneeName, line1, line2);
        if (byTown is not null) return byTown;
        return null;
    }
}
