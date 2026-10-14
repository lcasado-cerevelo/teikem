using System.Globalization;
using System.IO.Compression;
using System.Text;
using ExcelDataReader;
using Teikem.Domain.Orders;
using Teikem.Infrastructure.Exceptions;

namespace Teikem.Infrastructure.Services;

/// <summary>Formato del archivo de importación (Lote 45, F-B2).</summary>
public static class ImportFileFormats
{
    public const string Csv = "CSV";
    public const string Xlsx = "XLSX";
}

/// <summary>
/// Archivo leído: formato, hojas del libro (vacío en CSV), hoja leída, fila de encabezado (si la plantilla dice que la trae) y
/// filas de datos con la misma forma que el lector CSV (ordinal desde 1 después del encabezado y línea física del archivo).
/// </summary>
public sealed record ImportFileTable(string Format, IReadOnlyList<string> Sheets, string? Sheet, IReadOnlyList<string>? Header, IReadOnlyList<CsvRow> Rows);

/// <summary>
/// Lote 45 (F-B2): lector único de los archivos de importación: CSV en UTF-8 (CsvParser) o Excel .xlsx (ExcelDataReader, MIT).
/// Del libro se lee UNA hoja (la pedida por nombre o la primera) y cada celda se pasa a texto como la vería el usuario:
/// - número entero sin decimales («901»); con formato de ceros a la izquierda («00000») se rellena a ese largo (00901);
/// - fecha con hora cero → yyyy-MM-dd; fecha y hora → yyyy-MM-dd HH:mm; solo hora (serial &lt; 1) → HH:mm (ventanas horarias);
/// - verdadero/falso → true/false; texto tal cual.
/// Filas vacías ignoradas y los mismos límites del CSV (5.000 filas de datos, 2 MB de archivo; además el libro descomprimido no
/// puede pasar de 64 MB, defensa contra archivos inflados). El .xls viejo (binario) no se admite: 400 con el mensaje de formato.
/// </summary>
public static class ImportFileReader
{
    public const string UnsupportedFormatMessage = "Formato de archivo no admitido: use CSV o Excel (.xlsx).";
    public const string UnreadableExcelMessage = "No se pudo leer el archivo de Excel; ábralo y guárdelo otra vez como .xlsx.";
    public static string SheetNotFoundMessage(string sheet) => $"El libro no tiene la hoja '{sheet}'.";

    // ExcelDataReader arma su configuración con la página de códigos 1252 (aunque el .xlsx es UTF-8 y no la usa); en .NET 8 las
    // páginas de Windows vienen en el marco pero hay que registrarlas. Se hace una vez y solo agrega codificaciones.
    static ImportFileReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Tope del libro descomprimido (suma de las partes del .xlsx).</summary>
    public const long MaxUncompressedBytes = 64L * 1024 * 1024;

    /// <summary>¿Es un .xlsx? Firma ZIP (PK\x03\x04) o extensión .xlsx.</summary>
    public static bool LooksLikeXlsx(ReadOnlySpan<byte> bytes, string? fileName)
        => (bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04)
           || (fileName?.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>¿Es un .xls binario viejo (firma OLE D0 CF 11 E0) o trae esa extensión?</summary>
    public static bool LooksLikeXls(ReadOnlySpan<byte> bytes, string? fileName)
        => (bytes.Length >= 4 && bytes[0] == 0xD0 && bytes[1] == 0xCF && bytes[2] == 0x11 && bytes[3] == 0xE0)
           || (fileName?.EndsWith(".xls", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>
    /// Lee el archivo. CSV: delimitador y encabezado de la plantilla. Excel: la hoja pedida (sin distinguir mayúsculas) o la
    /// primera; una hoja inexistente → 400 'sheet'. Supera el límite → CsvLimitException (el servicio la traduce a 400).
    /// </summary>
    public static ImportFileTable Read(byte[] bytes, string? fileName, char delimiter, bool hasHeader, string? sheet, int maxRows = CsvParser.MaxRows)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length > CsvParser.MaxBytes) throw new CsvLimitException();
        var limit = maxRows + (hasHeader ? 1 : 0);

        if (LooksLikeXls(bytes, fileName) && !LooksLikeXlsx(bytes, fileName)) throw new ValidationException("file", UnsupportedFormatMessage);
        if (LooksLikeXlsx(bytes, fileName))
        {
            var (sheets, chosen, raw) = ReadXlsx(bytes, sheet, limit);
            return Build(ImportFileFormats.Xlsx, sheets, chosen, raw, hasHeader);
        }

        var rows = CsvParser.Parse(DecodeText(bytes), delimiter, hasHeader: false, maxRows: limit);
        return Build(ImportFileFormats.Csv, Array.Empty<string>(), null, rows.Select(r => (r.Line, r.Cells)).ToList(), hasHeader);
    }

    /// <summary>
    /// Texto de un CSV con la codificación detectada: marca de orden de bytes (UTF-8 / UTF-16) si la trae; si no, UTF-8 cuando los
    /// bytes son UTF-8 válido; si no, Windows-1252 (lo que guarda Excel en Windows al exportar «CSV»): así la ñ y los acentos llegan
    /// bien en los dos casos.
    /// </summary>
    public static string DecodeText(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        try { return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes); }
        catch (DecoderFallbackException) { return Encoding.GetEncoding(1252).GetString(bytes); }
    }

    private static ImportFileTable Build(string format, IReadOnlyList<string> sheets, string? sheet, IReadOnlyList<(int Line, IReadOnlyList<string> Cells)> raw, bool hasHeader)
    {
        var header = hasHeader && raw.Count > 0 ? raw[0].Cells : null;
        var data = raw.Skip(hasHeader ? 1 : 0).Select((r, i) => new CsvRow(i + 1, r.Line, r.Cells)).ToList();
        return new ImportFileTable(format, sheets, sheet, header, data);
    }

    private static (IReadOnlyList<string> Sheets, string Sheet, List<(int Line, IReadOnlyList<string> Cells)> Rows) ReadXlsx(byte[] bytes, string? sheet, int limit)
    {
        EnsureReasonableZip(bytes);
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = ExcelReaderFactory.CreateOpenXmlReader(stream);
            var sheets = new List<string>();
            do { sheets.Add(reader.Name ?? $"Hoja{sheets.Count + 1}"); } while (reader.NextResult());
            if (sheets.Count == 0) throw new ValidationException("file", UnreadableExcelMessage);

            var index = 0;
            if (!string.IsNullOrWhiteSpace(sheet))
            {
                index = sheets.FindIndex(s => string.Equals(s.Trim(), sheet.Trim(), StringComparison.OrdinalIgnoreCase));
                if (index < 0) throw new ValidationException("sheet", SheetNotFoundMessage(sheet.Trim()));
            }

            reader.Reset();
            for (var i = 0; i < index; i++) reader.NextResult();

            var rows = new List<(int Line, IReadOnlyList<string> Cells)>();
            var line = 0;
            while (reader.Read())
            {
                line++;
                var cells = new string[reader.FieldCount];
                for (var c = 0; c < cells.Length; c++) cells[c] = CellText(reader, c);
                if (cells.All(string.IsNullOrWhiteSpace)) continue; // fila vacía
                if (rows.Count >= limit) throw new CsvLimitException();
                // sin las celdas vacías del final (Excel suele reportar columnas de más)
                var last = Array.FindLastIndex(cells, x => !string.IsNullOrWhiteSpace(x));
                rows.Add((line, cells.Take(last + 1).Select(x => x.Trim()).ToArray()));
            }
            return (sheets, sheets[index], rows);
        }
        catch (TeikemException) { throw; }
        catch (CsvLimitException) { throw; }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ValidationException("file", UnreadableExcelMessage);
        }
    }

    /// <summary>Un .xlsx es un ZIP: se revisa que sea legible y que, descomprimido, no pase de MaxUncompressedBytes.</summary>
    private static void EnsureReasonableZip(byte[] bytes)
    {
        long total = 0;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                total += entry.Length;
                if (total > MaxUncompressedBytes) throw new CsvLimitException();
            }
        }
        catch (CsvLimitException) { throw; }
        catch (InvalidDataException) { throw new ValidationException("file", UnreadableExcelMessage); }
    }

    /// <summary>Texto de una celda de Excel (ver la regla del encabezado de la clase).</summary>
    internal static string CellText(IExcelDataReader reader, int column)
    {
        var value = reader.GetValue(column);
        switch (value)
        {
            case null: return string.Empty;
            case string s: return s;
            case DateTime d: return DateText(d);
            case TimeSpan ts: return TimeText(ts);
            case bool b: return b ? "true" : "false";
            case double n: return NumberText(n, SafeNumberFormat(reader, column));
            default: return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }
    }

    private static string? SafeNumberFormat(IExcelDataReader reader, int column)
    {
        try { return reader.GetNumberFormatString(column); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return null; }
    }

    /// <summary>Número de celda a texto: entero sin «.0»; formato de solo ceros («00000») rellena a su largo; decimal invariante.</summary>
    public static string NumberText(double n, string? numberFormat)
    {
        if (double.IsFinite(n) && Math.Abs(n) < 1e15 && Math.Abs(n - Math.Round(n)) < 1e-9)
        {
            var integer = ((long)Math.Round(n)).ToString(CultureInfo.InvariantCulture);
            var format = numberFormat?.Trim();
            if (n >= 0 && format is { Length: > 1 } && format.All(ch => ch == '0') && integer.Length < format.Length)
                return integer.PadLeft(format.Length, '0');
            return integer;
        }
        return n.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Fecha de celda a texto: solo hora (día base de Excel) → HH:mm; hora cero → yyyy-MM-dd; si no, yyyy-MM-dd HH:mm.</summary>
    public static string DateText(DateTime d)
    {
        // Excel guarda fecha y hora como número de días: 08:00 puede llegar como 07:59:59.9999; se redondea al segundo.
        d = new DateTime((d.Ticks + TimeSpan.TicksPerSecond / 2) / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, d.Kind);
        if (d.Date == new DateTime(1899, 12, 30) || d.Date == new DateTime(1899, 12, 31)) return TimeText(d.TimeOfDay);
        if (d.TimeOfDay == TimeSpan.Zero) return d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return d.ToString(d.Second == 0 ? "yyyy-MM-dd HH:mm" : "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static string TimeText(TimeSpan t)
    {
        // Excel guarda la hora como fracción del día: 08:00 puede llegar como 07:59:59.9999; se redondea al segundo.
        var rounded = TimeSpan.FromSeconds(Math.Round(t.TotalSeconds));
        var h = (int)rounded.TotalHours % 24;
        return rounded.Seconds == 0
            ? $"{h:00}:{rounded.Minutes:00}"
            : $"{h:00}:{rounded.Minutes:00}:{rounded.Seconds:00}";
    }
}
