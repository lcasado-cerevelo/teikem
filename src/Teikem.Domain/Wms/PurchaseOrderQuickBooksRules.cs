using System.Globalization;
using System.Text.RegularExpressions;
using Teikem.Domain.Constants;
using Teikem.Domain.Sales;

namespace Teikem.Domain.Wms;

/// <summary>
/// Lote 246 (Lote F del plan de Solutions) — reglas puras de las órdenes de compra frente a QuickBooks Desktop (archivo IIF PURCHORD): qué estatus se exportan, el estado
/// (la MISMA función <see cref="QuickBooksSync.State"/>), qué impide exportar, confirmar, marcar o «No pasó», el memo del archivo y los mensajes exactos (el manual y la FAQ
/// los citan). Exportar NO marca: la orden queda «exportada, por confirmar» hasta «Sí, ya están» (IN_QUICKBOOKS) o «No pasó» (vuelve a PENDING). Editar o cancelar una orden
/// ya exportada o marcada no la desmarca. Las que vinieron de QuickBooks nunca se exportan ni se marcan.
/// </summary>
public static class PurchaseOrderQuickBooksRules
{
    /// <summary>Se pasan a QuickBooks las enviadas, recibidas en parte y recibidas (con las cantidades pedidas). DRAFT y CANCELLED no.</summary>
    public static readonly IReadOnlySet<string> ExportableStatuses =
        new HashSet<string>(StringComparer.Ordinal) { PurchaseOrderStatuses.Sent, PurchaseOrderStatuses.Partial, PurchaseOrderStatuses.Received };

    /// <summary>Tope de órdenes por llamada al exportar, marcar o confirmar por lista (para «todas» está all = true).</summary>
    public const int MaxIdsPerCall = QuickBooksRules.MaxIdsPerCall;
    /// <summary>Largo máximo del memo de la orden en el archivo.</summary>
    public const int MaxMemoLength = 4000;

    // ---------------------------------------------------------------- mensajes (código HTTP en el comentario)
    /// <summary>422.</summary>
    public const string NothingPending = "No hay órdenes de compra por pasar a QuickBooks.";
    /// <summary>400.</summary>
    public const string SelectionRequired = "Indique las órdenes de compra (purchaseOrderIds) o pida todas (all = true).";
    /// <summary>400.</summary>
    public const string TooManyIds = "No se pueden marcar más de 1000 órdenes de compra en una sola llamada; use «todas».";
    /// <summary>400 (también si es de otra compañía: sin oráculo).</summary>
    public const string NotFound = "Alguna de las órdenes de compra indicadas no existe.";

    /// <summary>422.</summary>
    public static string FromQuickBooks(string number) => $"La orden de compra {number} vino de QuickBooks: no se exporta ni se marca.";
    /// <summary>422.</summary>
    public static string NotExportableStatus(string number, string status) =>
        $"La orden de compra {number} está en {status}: solo se pasan a QuickBooks las enviadas, recibidas en parte o recibidas.";
    /// <summary>409.</summary>
    public static string AlreadyInQuickBooks(string number) =>
        $"La orden de compra {number} ya está en QuickBooks; para volver a exportarla, quítele la marca.";
    /// <summary>422.</summary>
    public static string NotExported(string number) =>
        $"La orden de compra {number} no ha salido en ningún archivo de QuickBooks; para marcarla a mano use «Marcar como en QuickBooks».";
    /// <summary>409.</summary>
    public static string NotPassedMarked(string number) =>
        $"La orden de compra {number} ya está marcada como en QuickBooks: quítele la marca en vez de usar «No pasó».";
    /// <summary>400.</summary>
    public static string UnknownState(string? state) =>
        $"El estado de QuickBooks «{state?.Trim()}» no es válido: use PENDING, EXPORTED, IN_QUICKBOOKS, FROM_QUICKBOOKS o NOT_EXPORTABLE.";

    // ---------------------------------------------------------------- avisos (no bloquean)
    public static string UnmappedSupplierTerm(string code) =>
        $"El término «{code}» no tiene nombre en QuickBooks (Ajustes → Operación → QuickBooks): el proveedor nuevo sale sin término.";
    public static string RoundingDifference(string number, decimal teikem, decimal quickBooks) =>
        $"La orden de compra {number} suma {Money(teikem)} en Teikem y {Money(quickBooks)} en el archivo: QuickBooks redondea cada línea a centavos.";
    public static string CurrencyDiffers(string number, string currency, string tenantCurrency) =>
        $"La orden de compra {number} está en {currency} y la compañía en {tenantCurrency}: el archivo no lleva moneda y QuickBooks la toma en la de la compañía.";
    public static string UnencodableText(string where, string chars) =>
        $"{where} tiene caracteres que QuickBooks no admite ({chars}); en el archivo salen como «?».";

    /// <summary>Monto en cultura invariante: 2 decimales y, si los trae, hasta 4 (el total de Teikem no se redondea por línea).</summary>
    private static string Money(decimal value) => value.ToString("#,##0.00##", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- estado

    public static bool IsExportableStatus(string? statusCode) => statusCode is not null && ExportableStatuses.Contains(statusCode);

    public static bool IsFromQuickBooks(string? originCode) => PurchaseOrderOrigins.OrDefault(originCode) == PurchaseOrderOrigins.QuickBooks;

    /// <summary>El estado de la orden frente a QuickBooks (origen NULL = TEIKEM).</summary>
    public static string State(string? originCode, string? statusCode, DateTime? exportedAt, DateTime? quickBooksAt)
        => QuickBooksSync.State(IsFromQuickBooks(originCode), IsExportableStatus(statusCode), exportedAt, quickBooksAt);

    // ---------------------------------------------------------------- qué impide cada acción (null = se puede)

    /// <summary>
    /// Exportar: vino de QuickBooks → 422; ya marcada → 409 (quitarle la marca para reexportarla); estatus que no se exporta → 422 (una exportada y luego cancelada
    /// se confirma o se manda a «No pasó», no se reexporta). Una ya exportada en estatus válido SÍ se reexporta (aviso «ya salieron el …»).
    /// </summary>
    public static (int Status, string Message)? ExportError(string number, string? originCode, string? statusCode, string statusLabel, DateTime? quickBooksAt)
    {
        if (IsFromQuickBooks(originCode)) return (422, FromQuickBooks(number));
        if (quickBooksAt is not null) return (409, AlreadyInQuickBooks(number));
        if (!IsExportableStatus(statusCode)) return (422, NotExportableStatus(number, statusLabel));
        return null;
    }

    /// <summary>
    /// Marcar como en QuickBooks. Con <paramref name="confirm"/> («Sí, ya están») solo las exportadas (422 si no salió en ningún archivo), en cualquier estatus. A mano,
    /// las elegibles o las ya exportadas. Vino de QuickBooks → 422. Una ya marcada no es error: queda igual (el servicio la cuenta como sin cambio).
    /// </summary>
    public static (int Status, string Message)? MarkError(string number, string? originCode, string? statusCode, string statusLabel, DateTime? exportedAt, DateTime? quickBooksAt, bool confirm)
    {
        if (IsFromQuickBooks(originCode)) return (422, FromQuickBooks(number));
        if (quickBooksAt is not null) return null;
        if (confirm && exportedAt is null) return (422, NotExported(number));
        if (!confirm && exportedAt is null && !IsExportableStatus(statusCode)) return (422, NotExportableStatus(number, statusLabel));
        return null;
    }

    /// <summary>«No pasó»: vino de QuickBooks → 422; marcada → 409 (quitarle la marca). Una nunca exportada no es error: queda igual.</summary>
    public static (int Status, string Message)? NotPassedError(string number, string? originCode, DateTime? quickBooksAt)
    {
        if (IsFromQuickBooks(originCode)) return (422, FromQuickBooks(number));
        if (quickBooksAt is not null) return (409, NotPassedMarked(number));
        return null;
    }

    /// <summary>Quitar la marca: vino de QuickBooks → 422. Una sin marca no es error: queda igual. Al quitarla vuelve a EXPORTED (si salió en un archivo) o a PENDING.</summary>
    public static (int Status, string Message)? UnmarkError(string number, string? originCode)
        => IsFromQuickBooks(originCode) ? (422, FromQuickBooks(number)) : null;

    // ---------------------------------------------------------------- memo y filtros

    private static readonly Regex ImportMark = new(@"\[(?:QB|IMP):[^\]]*\]", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex Spaces = new(@"\s+", RegexOptions.Compiled);

    /// <summary>Memo de la orden en el archivo: las notas sin la marca de importación ([QB:…] / [IMP:…]), sin tabuladores ni saltos (espacios seguidos = uno) y con ≤ 4000 caracteres. Vacío = null.</summary>
    public static string? Memo(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes)) return null;
        var text = Spaces.Replace(ImportMark.Replace(notes, " "), " ").Trim();
        if (text.Length > MaxMemoLength) text = text[..MaxMemoLength].TrimEnd();
        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// Lee los estados pedidos en un filtro (repetibles o separados por comas, sin distinguir mayúsculas; vacíos se ignoran; sin repetir). Uno desconocido → el mensaje
    /// exacto (400) en <c>Error</c>.
    /// </summary>
    public static (IReadOnlyList<string> States, string? Error) ParseStates(IEnumerable<string?>? values)
    {
        var result = new List<string>();
        foreach (var raw in values ?? Array.Empty<string?>())
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var code = part.ToUpperInvariant();
                if (!QuickBooksSyncStates.All.Contains(code)) return (Array.Empty<string>(), UnknownState(part));
                if (!result.Contains(code)) result.Add(code);
            }
        }
        return (result, null);
    }
}
