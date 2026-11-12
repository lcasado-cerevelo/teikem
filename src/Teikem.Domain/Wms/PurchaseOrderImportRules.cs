using System.Globalization;
using System.Text;

namespace Teikem.Domain.Wms;

/// <summary>Una línea del archivo de importación ya leída (de QuickBooks o del formato genérico), sin resolver contra la base.</summary>
public sealed record PoImportLine(int Line, string? Type, string? Vendor, string? DocNumber, DateOnly? Date, DateOnly? ExpectedDate,
    string? Sku, string? Description, decimal? Qty, decimal? Cost);

/// <summary>
/// Lote 239 — reglas puras de la importación de órdenes de compra (origen genérico CSV/Excel o QuickBooks Desktop).
/// Principios dictados por Luis (2026-11-12): una orden de compra dice «se compró» y NO mueve inventario (eso lo hace el recibo);
/// los SKU de QuickBooks y los de Teikem deben ser los mismos; una orden con un producto o proveedor que no existe NO se importa
/// (se bloquea y se avisa por qué), nunca se da de alta un producto a medias; importar dos veces el mismo archivo no duplica.
/// Los mensajes son constantes o métodos estáticos porque el manual y la FAQ los citan.
/// </summary>
public static class PurchaseOrderImportRules
{
    public const string SourceGeneric = "GENERIC";
    public const string SourceQuickBooks = "QUICKBOOKS";
    public const string StatusDraft = "DRAFT";
    public const string StatusSent = "SENT";

    // Estados de un documento en el resultado
    public const string DocReady = "READY";
    public const string DocBlocked = "BLOCKED";
    public const string DocDuplicate = "DUPLICATE";
    public const string DocSkipped = "SKIPPED";
    public const string DocCreated = "CREATED";
    public const string DocError = "ERROR";

    // Códigos de aviso por línea o por documento
    public const string IssueMissingProduct = "MISSING_PRODUCT";
    public const string IssueAmbiguousProduct = "AMBIGUOUS_PRODUCT";
    public const string IssueMissingSupplier = "MISSING_SUPPLIER";
    public const string IssueInactiveSupplier = "INACTIVE_SUPPLIER";
    public const string IssueInvalid = "INVALID";
    public const string IssueNoLines = "NO_LINES";
    public const string IssueAlias = "SKU_ALIAS";
    public const string IssueZeroQty = "ZERO_QTY";
    public const string IssueMergedLines = "MERGED_LINES";
    public const string IssueExpectedIgnored = "EXPECTED_IGNORED";
    public const string IssueCostRounded = "COST_ROUNDED";

    public const int MaxDocuments = 2000;

    // ---------------------------------------------------------------- mensajes
    public const string SourceUnknown = "El origen de la importación no es válido: use GENERIC o QUICKBOOKS.";
    public const string StatusUnknown = "El estatus de las órdenes importadas debe ser DRAFT (borrador) o SENT (enviada).";
    public const string NoFile = "Elija el archivo a importar.";
    public const string NoDocuments = "El archivo no trae órdenes de compra.";
    public const string TooManyDocuments = "El archivo trae más de 2000 órdenes; divídalo e impórtelo por partes.";
    public const string NoLinesWithQty = "La orden no trae ninguna línea con cantidad mayor que cero.";
    public const string NoSupplierInFile = "La orden no indica el proveedor.";
    public const string NoSkuInFile = "La línea no trae el SKU del producto.";
    public static string MissingColumn(string column) => $"El archivo no tiene la columna '{column}'.";
    public static string NotAPurchase(string type) => $"El tipo «{type}» no es una orden ni una factura de compra; no se importa.";
    public static string MissingProduct(string sku, IReadOnlyList<string> similar)
        => similar.Count == 0
            ? $"Falta el producto {sku}: dé de alta el producto con ese SKU y vuelva a importar."
            : $"Falta el producto {sku} (SKU parecidos en Teikem: {string.Join(", ", similar)}): dé de alta ese SKU o corrija el archivo y vuelva a importar.";
    public static string AmbiguousProduct(string sku, IReadOnlyList<string> candidates)
        => $"El SKU {sku} coincide con más de un producto ({string.Join(", ", candidates)}); corrija el archivo.";
    public static string MissingSupplier(string vendor) => $"Falta el proveedor «{vendor}»: dé de alta el proveedor con ese nombre y vuelva a importar.";
    public static string InactiveSupplier(string vendor) => $"El proveedor «{vendor}» está dado de baja; no admite órdenes de compra nuevas.";
    public static string AliasUsed(string fileSku, string sku) => $"El SKU {fileSku} del archivo se tomó como {sku} de Teikem (misma clave sin guiones, espacios ni signos).";
    public static string ZeroQty(string sku) => $"La línea de {sku} tiene cantidad 0 y se omitió.";
    public static string MergedLines(string sku, int count) => $"El producto {sku} venía en {count} líneas; se sumaron en una sola.";
    public static string ExpectedIgnored(DateOnly expected, DateOnly orderDate) => $"La fecha de entrega {expected:yyyy-MM-dd} es anterior a la de la orden {orderDate:yyyy-MM-dd}; se dejó sin fecha esperada.";
    public static string CostRounded(string sku, decimal raw, decimal rounded)
        => $"El costo de {sku} ({raw.ToString("0.########", CultureInfo.InvariantCulture)}) tiene más de 4 decimales; se redondeó a {rounded.ToString("0.####", CultureInfo.InvariantCulture)}.";
    public static string Duplicate(string number) => $"Ya se importó antes (orden de compra {number}); no se vuelve a crear.";

    // ---------------------------------------------------------------- SKU

    /// <summary>
    /// Clave de comparación de un SKU: mayúsculas y solo letras y dígitos. Así «NECH 1001», «NECH-1001» y «nech1001» son el mismo SKU,
    /// y «PD-23*36/15/10» es «PD-23-36/15/10». Vacío si no queda nada.
    /// </summary>
    public static string SkuKey(string? sku)
    {
        if (string.IsNullOrWhiteSpace(sku)) return string.Empty;
        var sb = new StringBuilder(sku.Length);
        foreach (var ch in sku)
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToUpperInvariant(ch));
        return sb.ToString();
    }

    /// <summary>Clave de un nombre de proveedor (mismo criterio que el SKU).</summary>
    public static string NameKey(string? name) => SkuKey(name);

    /// <summary>
    /// La celda «Item» de QuickBooks trae «SKU (Descripción)»: se separan. Sin paréntesis, todo es el SKU. Con sub-ítem «Padre:Hijo» se queda el hijo.
    /// </summary>
    public static (string Sku, string Description) SplitQuickBooksItem(string? item)
    {
        var text = (item ?? string.Empty).Trim();
        if (text.Length == 0) return (string.Empty, string.Empty);
        var open = text.IndexOf(" (", StringComparison.Ordinal);
        if (open > 0 && text.EndsWith(')'))
            return (text[..open].Trim(), text[(open + 2)..^1].Trim());
        return (text, string.Empty);
    }

    /// <summary>SKU de Teikem con parecido al del archivo (uno contiene al otro, con la clave sin signos). Máximo 3, para el aviso.</summary>
    public static IReadOnlyList<string> SimilarSkus(string fileSku, IEnumerable<string> teikemSkus)
    {
        var key = SkuKey(fileSku);
        if (key.Length < 4) return Array.Empty<string>();
        return teikemSkus
            .Where(s => { var k = SkuKey(s); return k.Length >= 4 && k != key && (key.Contains(k, StringComparison.Ordinal) || k.Contains(key, StringComparison.Ordinal)); })
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(3).ToList();
    }

    // ---------------------------------------------------------------- valores

    /// <summary>Número del archivo: quita «$», comas y espacios; punto decimal. Vacío o inválido → null.</summary>
    public static decimal? ParseNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim().Replace("$", "").Replace(",", "").Replace(" ", "");
        if (t.StartsWith('(') && t.EndsWith(')')) t = "-" + t[1..^1];
        return decimal.TryParse(t, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    /// <summary>Fecha del archivo: MM/dd/yyyy (QuickBooks) o yyyy-MM-dd (genérico y Excel). Otra cosa → null.</summary>
    public static DateOnly? ParseDate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var t = text.Trim();
        if (t.Length > 10 && t.Contains(' ')) t = t[..t.IndexOf(' ')];
        return DateOnly.TryParseExact(t, new[] { "MM/dd/yyyy", "M/d/yyyy", "yyyy-MM-dd" }, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    // ---------------------------------------------------------------- documentos

    /// <summary>Clave del documento dentro del archivo: proveedor + número (o, sin número, proveedor + fecha).</summary>
    public static string DocKey(string? vendor, string? number, DateOnly? date)
        => $"{NameKey(vendor)}|{(string.IsNullOrWhiteSpace(number) ? (date?.ToString("yyyyMMdd") ?? "") : number.Trim().ToUpperInvariant())}";

    /// <summary>
    /// Marca que se guarda en las notas de la orden creada para reconocerla si se vuelve a importar el mismo archivo
    /// («[QB:proveedor|número]»): la identidad de un documento importado es proveedor + número.
    /// </summary>
    public static string Marker(string source, string? vendor, string? number, DateOnly? date)
        => $"[{(source == SourceQuickBooks ? "QB" : "IMP")}:{DocKey(vendor, number, date)}]";

    /// <summary>Notas de la orden creada: de dónde viene y la marca de importación (máximo 2000 caracteres).</summary>
    public static string BuildNotes(string source, string? vendor, string? number, DateOnly? date)
    {
        var origin = source == SourceQuickBooks ? "QuickBooks" : "archivo";
        var doc = string.IsNullOrWhiteSpace(number) ? string.Empty : $" {number.Trim()}";
        var notes = $"Importada de {origin}{doc}. {Marker(source, vendor, number, date)}";
        return notes.Length <= PurchaseOrderRules.MaxNotesLength ? notes : notes[..PurchaseOrderRules.MaxNotesLength];
    }

    /// <summary>Tipos de transacción de QuickBooks que se importan como orden de compra: la orden y, para pruebas con historial, la factura de compra.</summary>
    public static bool IsPurchaseType(string? type)
        => string.Equals(type?.Trim(), "Purchase Order", StringComparison.OrdinalIgnoreCase) || string.Equals(type?.Trim(), "Bill", StringComparison.OrdinalIgnoreCase);

    public static string NormalizeStatus(string? status)
    {
        var s = (status ?? StatusDraft).Trim().ToUpperInvariant();
        return s is StatusDraft or StatusSent ? s : string.Empty;
    }

    public static string NormalizeSource(string? source)
    {
        var s = (source ?? SourceGeneric).Trim().ToUpperInvariant();
        return s is SourceGeneric or SourceQuickBooks ? s : string.Empty;
    }
}
