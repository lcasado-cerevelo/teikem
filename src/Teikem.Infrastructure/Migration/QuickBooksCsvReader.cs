using System.Text;
using Teikem.Infrastructure.Exceptions;

namespace Teikem.Infrastructure.Migration;

/// <summary>
/// Fila de datos de una lista de QuickBooks. <see cref="Get"/> devuelve la celda recortada por nombre de encabezado
/// (sin distinguir mayúsculas), cadena vacía si la columna existe y la fila no trae esa celda, o null si la columna no existe.
/// </summary>
public sealed class QbRow
{
    private readonly IReadOnlyDictionary<string, int> _index;

    internal QbRow(IReadOnlyDictionary<string, int> index, IReadOnlyList<string> cells, int line)
    {
        _index = index;
        Cells = cells;
        Line = line;
    }

    /// <summary>Línea física del archivo donde empieza la fila (la cabecera es la línea 1).</summary>
    public int Line { get; }

    /// <summary>Celdas sin la primera columna vacía de QuickBooks.</summary>
    public IReadOnlyList<string> Cells { get; }

    public bool Has(string header) => _index.ContainsKey(header);

    public string? Get(string header)
    {
        if (!_index.TryGetValue(header, out var i)) return null;
        return i < Cells.Count ? Cells[i].Trim() : string.Empty;
    }

    /// <summary>Como <see cref="Get"/>, pero la celda vacía también se devuelve como null.</summary>
    public string? GetOrNull(string header) => Get(header) is { Length: > 0 } v ? v : null;
}

/// <summary>Archivo de QuickBooks ya leído: nombre, encabezados (sin la primera columna vacía) y filas de datos.</summary>
public sealed record QbTable(string FileName, IReadOnlyList<string> Headers, IReadOnlyList<QbRow> Rows);

/// <summary>Ítem de QuickBooks (Item List). Todas las celdas vienen recortadas; vacío = null. Los números quedan como texto.</summary>
public sealed record QbItem(
    string? ActiveStatus,
    string? Type,
    string? Item,
    string? Description,
    string? QuantityOnHand,
    string? Cost,
    string? Price,
    string? Category,
    string? Brand,
    string? Manufacturer,
    string? Mpn)
{
    /// <summary>Línea física de la fila en el archivo, para el reporte.</summary>
    public int Line { get; init; }

    public bool IsActive => QuickBooksCsvReader.IsActiveStatus(ActiveStatus);
}

/// <summary>Cliente de QuickBooks (Customer List). BillTo/ShipTo traen siempre 5 líneas (null si vacías).</summary>
public sealed record QbCustomer(
    string? ActiveStatus,
    string? Customer,
    string? Company,
    string? FirstName,
    string? LastName,
    string? MainPhone,
    string? AltPhone,
    string? MainEmail,
    IReadOnlyList<string?> BillTo,
    IReadOnlyList<string?> ShipTo,
    string? Terms,
    string? Rep,
    string? CreditLimit)
{
    public int Line { get; init; }

    public bool IsActive => QuickBooksCsvReader.IsActiveStatus(ActiveStatus);
}

/// <summary>Proveedor de QuickBooks (Vendor List). BillFrom trae siempre 5 líneas (null si vacías).</summary>
public sealed record QbVendor(
    string? ActiveStatus,
    string? Vendor,
    string? Company,
    IReadOnlyList<string?> BillFrom,
    string? PrimaryContact,
    string? MainPhone,
    string? FirstName,
    string? LastName)
{
    public int Line { get; init; }

    public bool IsActive => QuickBooksCsvReader.IsActiveStatus(ActiveStatus);
}

/// <summary>
/// Lote 10 (P2): lector de las listas exportadas de QuickBooks Desktop (Items, Customers, Vendors).
/// Detecta la codificación (UTF-8 con BOM o estricto; si falla, Windows-1252), parsea CSV RFC-4180 con un parser propio
/// sin tope de filas (comillas dobles escapadas, saltos de línea dentro de comillas, CRLF), ignora la primera columna
/// sin nombre que QuickBooks antepone a cada fila y las filas totalmente vacías. Solo lee: no toca la base.
/// </summary>
public static class QuickBooksCsvReader
{
    public static readonly string[] ItemRequiredColumns =
        { "Active Status", "Type", "Item", "Description", "Quantity On Hand", "Cost", "Price" };

    public static readonly string[] CustomerRequiredColumns =
        { "Active Status", "Customer", "Company", "Main Phone", "Main Email", "Bill to 1", "Ship to 1", "Terms" };

    public static readonly string[] VendorRequiredColumns =
        { "Active Status", "Vendor", "Company", "Bill from 1", "Main Phone" };

    /// <summary>Mensaje exacto cuando falta un encabezado obligatorio.</summary>
    public static string MissingColumn(string fileName, string column) => $"El archivo {fileName} no tiene la columna '{column}'.";

    internal static bool IsActiveStatus(string? status) => string.Equals(status?.Trim(), "Active", StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------- lectura de archivos

    /// <summary>Lee el archivo y devuelve sus filas de datos.</summary>
    public static IReadOnlyList<QbRow> Read(string path) => ReadTable(path).Rows;

    /// <summary>Lee el archivo completo (encabezados y filas).</summary>
    public static QbTable ReadTable(string path)
    {
        if (!File.Exists(path)) throw new ValidationException(LegacyImportConfig.FileNotFound(path));
        return ParseTable(Decode(File.ReadAllBytes(path)), Path.GetFileName(path));
    }

    public static IReadOnlyList<QbItem> ReadItems(string path) => ToItems(ReadTable(path));
    public static IReadOnlyList<QbCustomer> ReadCustomers(string path) => ToCustomers(ReadTable(path));
    public static IReadOnlyList<QbVendor> ReadVendors(string path) => ToVendors(ReadTable(path));

    // ---------------------------------------------------------------- lectura desde texto (pruebas)

    public static IReadOnlyList<QbItem> ParseItems(string content, string fileName) => ToItems(ParseTable(content, fileName));
    public static IReadOnlyList<QbCustomer> ParseCustomers(string content, string fileName) => ToCustomers(ParseTable(content, fileName));
    public static IReadOnlyList<QbVendor> ParseVendors(string content, string fileName) => ToVendors(ParseTable(content, fileName));

    // ---------------------------------------------------------------- codificación

    /// <summary>
    /// Decodifica los bytes del archivo: BOM UTF-8/UTF-16 si lo hay; si no, UTF-8 estricto; si hay bytes inválidos
    /// para UTF-8, Windows-1252 (registrando CodePagesEncodingProvider).
    /// </summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Windows1252().GetString(bytes);
        }
    }

    /// <summary>Codificación Windows-1252 (requiere registrar el proveedor de páginas de códigos en .NET).</summary>
    public static Encoding Windows1252()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }

    // ---------------------------------------------------------------- parser

    /// <summary>
    /// Parsea el contenido: la primera fila no vacía es la cabecera. Si su primera celda está vacía (formato de
    /// QuickBooks: la cabecera empieza con coma) se descarta esa columna en todas las filas.
    /// </summary>
    public static QbTable ParseTable(string? content, string fileName)
    {
        var text = content ?? string.Empty;
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];

        List<string>? headers = null;
        var skipFirst = false;
        Dictionary<string, int>? index = null;
        var rows = new List<QbRow>();

        foreach (var (cells, line) in ParseRecords(text))
        {
            if (cells.All(string.IsNullOrWhiteSpace)) continue; // fila totalmente vacía

            if (headers is null)
            {
                skipFirst = cells.Count > 0 && string.IsNullOrWhiteSpace(cells[0]);
                headers = (skipFirst ? cells.Skip(1) : cells).Select(h => h.Trim()).ToList();
                index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < headers.Count; i++)
                    if (headers[i].Length > 0) index.TryAdd(headers[i], i);
                continue;
            }

            var data = skipFirst ? cells.Skip(1).ToList() : cells;
            if (data.All(string.IsNullOrWhiteSpace)) continue;
            rows.Add(new QbRow(index!, data, line));
        }

        return new QbTable(fileName, (IReadOnlyList<string>?)headers ?? Array.Empty<string>(), rows);
    }

    /// <summary>
    /// Registros lógicos RFC-4180 con la línea física donde empieza cada uno. Una celda que empieza con comilla se lee
    /// hasta la comilla de cierre ("" = comilla literal) y puede contener comas y saltos de línea; CRLF, LF o CR cierran
    /// el registro fuera de comillas. Sin tope de filas.
    /// </summary>
    public static IEnumerable<(List<string> Cells, int Line)> ParseRecords(string text)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;
        var any = false;          // el registro actual tiene contenido (celdas, texto o comillas)
        var line = 1;
        var recordLine = 1;
        var i = 0;

        while (i < text.Length)
        {
            var ch = text[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i += 2; continue; }
                    inQuotes = false; i++; continue;
                }
                if (ch == '\r')
                {
                    // CRLF dentro de comillas se conserva como un solo salto '\n'
                    cell.Append('\n');
                    line++;
                    i += i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                    continue;
                }
                if (ch == '\n') line++;
                cell.Append(ch); i++; continue;
            }

            if (ch == '"' && cell.Length == 0) { inQuotes = true; any = true; i++; continue; }
            if (ch == ',') { cells.Add(cell.ToString()); cell.Clear(); any = true; i++; continue; }
            if (ch == '\r' || ch == '\n')
            {
                cells.Add(cell.ToString());
                cell.Clear();
                yield return (cells, recordLine);
                cells = new List<string>();
                any = false;
                i += ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1;
                line++;
                recordLine = line;
                continue;
            }
            cell.Append(ch); any = true; i++;
        }

        if (any || cell.Length > 0)
        {
            cells.Add(cell.ToString());
            yield return (cells, recordLine);
        }
    }

    // ---------------------------------------------------------------- proyecciones tipadas

    private static void Require(QbTable table, IEnumerable<string> columns)
    {
        var set = new HashSet<string>(table.Headers, StringComparer.OrdinalIgnoreCase);
        foreach (var col in columns)
            if (!set.Contains(col)) throw new ValidationException(MissingColumn(table.FileName, col));
    }

    private static IReadOnlyList<string?> Lines(QbRow r, string prefix)
        => Enumerable.Range(1, 5).Select(n => r.GetOrNull($"{prefix} {n}")).ToArray();

    private static IReadOnlyList<QbItem> ToItems(QbTable table)
    {
        Require(table, ItemRequiredColumns);
        return table.Rows.Select(r => new QbItem(
            r.GetOrNull("Active Status"),
            r.GetOrNull("Type"),
            r.GetOrNull("Item"),
            r.GetOrNull("Description"),
            r.GetOrNull("Quantity On Hand"),
            r.GetOrNull("Cost"),
            r.GetOrNull("Price"),
            r.GetOrNull("Category"),
            r.GetOrNull("Brand"),
            // Solutions exporta la columna con la errata 'MANUFACTERS'
            r.GetOrNull("Manufacturer") ?? r.GetOrNull("MANUFACTERS"),
            r.GetOrNull("MPN"))
        { Line = r.Line }).ToList();
    }

    private static IReadOnlyList<QbCustomer> ToCustomers(QbTable table)
    {
        Require(table, CustomerRequiredColumns);
        return table.Rows.Select(r => new QbCustomer(
            r.GetOrNull("Active Status"),
            r.GetOrNull("Customer"),
            r.GetOrNull("Company"),
            r.GetOrNull("First Name"),
            r.GetOrNull("Last Name"),
            r.GetOrNull("Main Phone"),
            r.GetOrNull("Alt. Phone"),
            r.GetOrNull("Main Email"),
            Lines(r, "Bill to"),
            Lines(r, "Ship to"),
            r.GetOrNull("Terms"),
            r.GetOrNull("Rep"),
            r.GetOrNull("Credit Limit"))
        { Line = r.Line }).ToList();
    }

    private static IReadOnlyList<QbVendor> ToVendors(QbTable table)
    {
        Require(table, VendorRequiredColumns);
        return table.Rows.Select(r => new QbVendor(
            r.GetOrNull("Active Status"),
            r.GetOrNull("Vendor"),
            r.GetOrNull("Company"),
            Lines(r, "Bill from"),
            r.GetOrNull("Primary Contact"),
            r.GetOrNull("Main Phone"),
            r.GetOrNull("First Name"),
            r.GetOrNull("Last Name"))
        { Line = r.Line }).ToList();
    }
}
