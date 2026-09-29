namespace Teikem.Domain.Migration;

/// <summary>
/// Lote 10 (P1) — dirección de QuickBooks Desktop ya descompuesta a partir de sus líneas 'Bill to 1..5' / 'Ship to 1..5'.
/// Name = línea 1; City/State/PostalCode salen de la línea 'Ciudad, PR 00xxx'; Phone de la última línea con forma de
/// teléfono; Line1/Line2 de las líneas intermedias restantes. Sin línea de ciudad: City = 'SIN CIUDAD' y State = 'PR'.
/// </summary>
public sealed record QuickBooksAddress(
    string? Name, string? Line1, string? Line2, string City, string State, string? PostalCode, string? Phone)
{
    /// <summary>¿Se encontró una línea de ciudad? (false → City = 'SIN CIUDAD' y el reporte lo informa).</summary>
    public bool CityParsed => !string.Equals(City, LegacyImportRules.NoCity, StringComparison.Ordinal);

    /// <summary>Todas las líneas venían vacías (no hay dirección).</summary>
    public bool IsEmpty => Name is null;
}

/// <summary>
/// Lote 10 (P1) — código de posición del WMS normalizado. Para el patrón 'NN-L-NN' ('01-a-24') trae además pasillo, nivel y
/// posición; para cualquier otro id (PISO, R1, 0101...) solo el código.
/// </summary>
public sealed record BinParts(string Code, string? Aisle, string? Level, string? Position);

/// <summary>
/// Lote 10 (P1) — regla de zona destino de la configuración del importador: una posición del WMS cae en la zona Code si su id
/// está en MatchLocationIds (igualdad sin distinguir mayúsculas) o, si no, si su descripción empieza con MatchDescription.
/// ZoneType es el código interno del catálogo de tipos de zona (PICKING, RESERVE, STAGING...).
/// </summary>
public sealed record ZoneRule(string Code, string Name, string ZoneType, string? MatchDescription, IReadOnlyList<string>? MatchLocationIds);
