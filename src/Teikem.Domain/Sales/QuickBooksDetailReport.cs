using System.Globalization;
using System.Text;

namespace Teikem.Domain.Sales;

/// <summary>
/// Una transacción de un reporte «by Item Detail» de QuickBooks: sección (Inventory, Parts, Service, Discounts…), encabezado del ítem tal cual («SKU (descripción)»),
/// SKU y descripción separados, tipo (Invoice, Credit Memo, Bill, Item Receipt, Credit), fecha, número, memo, nombre (cliente o suplidor), cantidad, precio (null si viene
/// vacío o en %) y monto. Line = línea física del archivo.
/// </summary>
public sealed record QbDetailRow(int Line, string Section, string ItemHeader, string ItemSku, string? ItemDescription, string Type, DateOnly Date, string? Number,
    string? Memo, string Name, decimal Quantity, decimal? Price, decimal Amount);

/// <summary>Reporte leído: si es de ventas o de compras (por la columna de precio), sus transacciones y los avisos (totales que no cuadran, filas sin fecha…).</summary>
public sealed record QbDetailReport(string Kind, IReadOnlyList<QbDetailRow> Rows, IReadOnlyList<string> Warnings, decimal ReportTotal);

/// <summary>
/// Lote 241 — lector PURO de los reportes «Sales by Item Detail» y «Purchases by Item Detail» exportados a CSV desde QuickBooks Desktop. El formato:
/// la cabecera empieza con una columna sin nombre (,"Type","Date","Num","Memo","Name"|"Source Name","Qty","Sales Price"|"Cost Price","Amount","Balance"); los renglones con
/// texto SOLO en esa primera columna son encabezados anidados (sección → ítem «SKU (descripción)») que se cierran con «Total …»; «TOTAL» cierra el reporte; los
/// renglones con la primera columna vacía y Type son transacciones del ítem abierto. CSV RFC-4180 (comillas, "" = comilla, comas y saltos dentro de comillas); el
/// archivo viene en Windows-1252 (<see cref="Decode"/> detecta UTF-8). Fechas MM/DD/AAAA. Números con punto, sin separador de miles (se toleran comas, paréntesis
/// negativos y el signo %, que deja el precio en null). Comprueba que la suma de cada ítem cuadre con su «Total».
/// </summary>
public static class QuickBooksDetailReportParser
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Bytes del archivo a texto: BOM UTF-8/UTF-16, si no UTF-8 estricto y, si falla, Windows-1252.</summary>
    public static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1252).GetString(bytes);
        }
    }

    /// <summary>Huella SHA-256 (hex en minúsculas) del contenido: reimportar el mismo archivo reemplaza su lote.</summary>
    public static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>
    /// Lee el reporte. Lanza <see cref="FormatException"/> con <see cref="SalesAnalysisRules.NotAReport"/> si no encuentra la cabecera (Type, Date, Qty, Amount).
    /// </summary>
    public static QbDetailReport Parse(string text, string fileName)
    {
        var records = ParseRecords(text ?? string.Empty).ToList();
        int headerIdx = -1;
        Dictionary<string, int>? col = null;
        for (var i = 0; i < records.Count && headerIdx < 0; i++)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var c = 0; c < records[i].Cells.Count; c++)
            {
                var h = records[i].Cells[c].Trim();
                if (h.Length > 0) map.TryAdd(h, c);
            }
            if (map.ContainsKey("Type") && map.ContainsKey("Date") && map.ContainsKey("Qty") && map.ContainsKey("Amount")) { headerIdx = i; col = map; }
        }
        if (col is null) throw new FormatException(SalesAnalysisRules.NotAReport(fileName));

        var kind = col.ContainsKey("Cost Price") || col.ContainsKey("Source Name") ? SalesAnalysisCodes.KindPurchases : SalesAnalysisCodes.KindSales;
        int Col(params string[] names) { foreach (var n in names) if (col.TryGetValue(n, out var i)) return i; return -1; }
        var iType = Col("Type"); var iDate = Col("Date"); var iNum = Col("Num"); var iMemo = Col("Memo"); var iName = Col("Name", "Source Name");
        var iQty = Col("Qty"); var iPrice = Col("Sales Price", "Cost Price", "Price"); var iAmount = Col("Amount");
        var dataCols = new[] { iType, iDate, iNum, iMemo, iName, iQty, iPrice, iAmount }.Where(i => i > 0).ToArray();

        var rows = new List<QbDetailRow>();
        var warnings = new List<string>();
        var stack = new List<string>();
        var sums = new Dictionary<string, (decimal Qty, decimal Amount)>(StringComparer.Ordinal);
        decimal reportTotal = 0m;

        string Cell(IReadOnlyList<string> cells, int i) => i >= 0 && i < cells.Count ? cells[i].Trim() : string.Empty;

        for (var r = headerIdx + 1; r < records.Count; r++)
        {
            var (cells, line) = records[r];
            if (cells.All(string.IsNullOrWhiteSpace)) continue;
            var first = Cell(cells, 0);
            var type = Cell(cells, iType);
            if (first.Length > 0)
            {
                if (string.Equals(first, "TOTAL", StringComparison.Ordinal))
                {
                    reportTotal = ParseNumber(Cell(cells, iAmount)) ?? 0m;
                    break;
                }
                if (first.StartsWith("Total ", StringComparison.Ordinal))
                {
                    var name = first[6..];
                    var idx = stack.LastIndexOf(name);
                    if (idx < 0)
                    {
                        warnings.Add($"Línea {line}: «{first}» no cierra ningún encabezado abierto.");
                        continue;
                    }
                    var key = string.Join("\u001f", stack.Take(idx + 1));
                    if (sums.TryGetValue(key, out var s))
                    {
                        var totAmount = ParseNumber(Cell(cells, iAmount));
                        if (totAmount is decimal ta && Math.Abs(ta - s.Amount) > 0.005m)
                            warnings.Add(string.Create(Inv, $"Línea {line}: el total de «{name}» dice {ta:0.00} y la suma de sus renglones da {s.Amount:0.00}."));
                    }
                    stack.RemoveRange(idx, stack.Count - idx);
                    continue;
                }
                if (dataCols.All(i => Cell(cells, i).Length == 0)) { stack.Add(first); continue; }
            }
            if (type.Length == 0) continue;
            if (stack.Count == 0) { warnings.Add($"Línea {line}: transacción fuera de un ítem; se omite."); continue; }
            var rawDate = Cell(cells, iDate);
            if (!DateOnly.TryParseExact(rawDate, new[] { "MM/dd/yyyy", "M/d/yyyy" }, Inv, DateTimeStyles.None, out var date))
            {
                warnings.Add($"Línea {line}: fecha «{rawDate}» no válida; se omite.");
                continue;
            }
            var header = stack[^1];
            var (sku, desc) = SplitItem(header);
            var qty = ParseNumber(Cell(cells, iQty)) ?? 0m;
            var amount = ParseNumber(Cell(cells, iAmount)) ?? 0m;
            var rawPrice = Cell(cells, iPrice);
            var price = rawPrice.Contains('%') ? null : ParseNumber(rawPrice);
            rows.Add(new QbDetailRow(line, stack[0], header, sku, desc, type, date, NullIfEmpty(Cell(cells, iNum)), NullIfEmpty(Cell(cells, iMemo)), Cell(cells, iName),
                qty, price, amount));
            for (var d = 1; d <= stack.Count; d++)
            {
                var key = string.Join("\u001f", stack.Take(d));
                var prev = sums.GetValueOrDefault(key);
                sums[key] = (prev.Qty + qty, prev.Amount + amount);
            }
        }
        return new QbDetailReport(kind, rows, warnings, reportTotal);
    }

    /// <summary>«SKU (descripción)» → (SKU, descripción); sin paréntesis final = (todo, null).</summary>
    public static (string Sku, string? Description) SplitItem(string header)
    {
        var h = (header ?? string.Empty).Trim();
        var i = h.IndexOf(" (", StringComparison.Ordinal);
        if (i > 0 && h.EndsWith(')')) return (h[..i].Trim(), h[(i + 2)..^1].Trim());
        return (h, null);
    }

    /// <summary>Número de QuickBooks: punto decimal, se toleran comas de miles, «(12.50)» negativo y el signo %. Vacío o inválido = null.</summary>
    public static decimal? ParseNumber(string? raw)
    {
        var s = raw?.Trim().Replace(",", string.Empty).Replace("%", string.Empty).Replace("$", string.Empty);
        if (string.IsNullOrEmpty(s)) return null;
        var negative = s.StartsWith('(') && s.EndsWith(')');
        if (negative) s = s[1..^1];
        return decimal.TryParse(s, NumberStyles.Float, Inv, out var v) ? (negative ? -v : v) : null;
    }

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    /// <summary>Registros RFC-4180 con la línea física donde empieza cada uno (comillas dobles, "" = comilla literal, comas y saltos dentro de comillas; CRLF/LF/CR).</summary>
    public static IEnumerable<(List<string> Cells, int Line)> ParseRecords(string text)
    {
        var cells = new List<string>();
        var cell = new StringBuilder();
        var inQuotes = false;
        var any = false;
        var line = 1;
        var recordLine = 1;
        var i = 0;
        if (text.Length > 0 && text[0] == '﻿') i = 1;
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
                if (ch == '\r') { cell.Append('\n'); line++; i += i + 1 < text.Length && text[i + 1] == '\n' ? 2 : 1; continue; }
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
}
