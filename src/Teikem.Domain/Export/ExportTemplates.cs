using System.Globalization;
using System.Text;
using System.Text.Json;
using Teikem.Domain.Common;

namespace Teikem.Domain.Export;

/// <summary>
/// Lote 77 (P5) — plantillas de exportación a contabilidad (ACCT_TEMPLATES del diseño): un formato configurable (CSV o TXT, delimitador, formato de fecha, encabezado, columnas y su orden) que arma el
/// archivo de una corrida aprobada sin atar el motor a un formato fijo (QuickBooks u otro se ajusta luego con una muestra real). El mismo motor sirve a Facturación hoy y a la contabilización de compras y
/// de despachos y a la liquidación de choferes después (un alcance por cada uno).
/// </summary>
public static class ExportCodes
{
    /// <summary>EntityType de la bitácora de las plantillas (sembrado en la sección 2026-10-16 (u) de update.sql).</summary>
    public const string EntityType = "EXPORT_TEMPLATE";

    /// <summary>Alcances: qué documento exporta la plantilla. Hoy solo Facturación; los demás se suman con su pantalla.</summary>
    public const string ScopeBilling = "BILLING";
    /// <summary>Lote 86 (P8): la remesa COD al cliente (una fila por cobro remitido).</summary>
    public const string ScopeCodRemittance = "COD_REMITTANCE";
    /// <summary>Lote 95 (P9): la liquidación a choferes (una fila por línea: entrega con sus intentos o viaje).</summary>
    public const string ScopeDriverSettlement = "DRIVER_SETTLEMENT";
    public static readonly string[] Scopes = { ScopeBilling, ScopeCodRemittance, ScopeDriverSettlement };

    public const string FormatCsv = "CSV";
    public const string FormatTxt = "TXT";
    public static readonly string[] Formats = { FormatCsv, FormatTxt };

    /// <summary>Delimitadores permitidos: coma, punto y coma, tabulador y barra vertical.</summary>
    public static readonly string[] Delimiters = { ",", ";", "\t", "|" };

    public const string DateIso = "YYYY-MM-DD";
    public const string DateUs = "MM/DD/YYYY";
    public const string DateEu = "DD/MM/YYYY";
    public static readonly string[] DateFormats = { DateIso, DateUs, DateEu };

    public const string DefaultTemplateName = "Formato estándar";
}

/// <summary>Una plantilla guardada. Por compañía y alcance; el nombre no se repite entre las activas (sin distinguir mayúsculas ni acentos). Nunca se borra: se desactiva.</summary>
[AuditEntity(ExportCodes.EntityType)]
public class ExportTemplate : ITenantScoped, ISoftDeletable, IAuditStamped
{
    public int ExportTemplateId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string Scope { get; set; } = ExportCodes.ScopeBilling;
    public string Name { get; set; } = string.Empty;
    public string Format { get; set; } = ExportCodes.FormatCsv;
    /// <summary>Un carácter: coma, punto y coma, tabulador o barra vertical.</summary>
    public string Delimiter { get; set; } = ",";
    public string DateFormat { get; set; } = ExportCodes.DateIso;
    public bool IncludeHeader { get; set; } = true;
    /// <summary>Las columnas en orden: arreglo JSON de {"field": código, "header": texto opcional}.</summary>
    public string ColumnsJson { get; set; } = "[]";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }
}

/// <summary>Una columna de la plantilla: el campo del catálogo y, si se quiere, el texto del encabezado (por omisión, el del catálogo).</summary>
public sealed record ExportColumn(string Field, string? Header = null);

public enum ExportKind { Text, Date, Money, Number }

/// <summary>Un campo exportable: código, tipo, encabezado por omisión (en inglés, como lo piden los programas de contabilidad) y su etiqueta para la pantalla.</summary>
public sealed record ExportField(string Code, ExportKind Kind, string DefaultHeader, string LabelEs, string LabelEn);

public static class ExportFields
{
    // ---- Facturación: una fila por línea de factura (con los datos de la factura y de la corrida repetidos en cada una)
    public static readonly IReadOnlyList<ExportField> Billing = new[]
    {
        new ExportField("INVOICE_NUMBER", ExportKind.Text, "InvoiceNo", "Número de factura", "Invoice number"),
        new ExportField("ISSUE_DATE", ExportKind.Date, "InvoiceDate", "Fecha de emisión", "Issue date"),
        new ExportField("DUE_DATE", ExportKind.Date, "DueDate", "Fecha de vencimiento", "Due date"),
        new ExportField("CLIENT_CODE", ExportKind.Text, "CustomerCode", "Código del cliente", "Customer code"),
        new ExportField("CLIENT_NAME", ExportKind.Text, "Customer", "Cliente", "Customer"),
        new ExportField("CURRENCY", ExportKind.Text, "Currency", "Moneda", "Currency"),
        new ExportField("LINE_NO", ExportKind.Number, "LineNo", "Número de línea", "Line number"),
        new ExportField("CHARGE_TYPE", ExportKind.Text, "ChargeType", "Tipo de cargo", "Charge type"),
        new ExportField("DESCRIPTION", ExportKind.Text, "Description", "Descripción", "Description"),
        new ExportField("ORDER_NUMBER", ExportKind.Text, "OrderNo", "Número de orden", "Order number"),
        new ExportField("SKU", ExportKind.Text, "Item", "Código del producto", "Item code"),
        new ExportField("QUANTITY", ExportKind.Number, "Qty", "Cantidad", "Quantity"),
        new ExportField("UNIT_AMOUNT", ExportKind.Number, "Rate", "Monto unitario", "Unit amount"),
        new ExportField("LINE_SUBTOTAL", ExportKind.Money, "Amount", "Subtotal de la línea", "Line subtotal"),
        new ExportField("TAX_RATE_PCT", ExportKind.Number, "TaxRate", "Tasa de impuesto (%)", "Tax rate (%)"),
        new ExportField("TAX_AMOUNT", ExportKind.Money, "Tax", "Impuesto de la línea", "Line tax"),
        new ExportField("LINE_TOTAL", ExportKind.Money, "LineTotal", "Total de la línea", "Line total"),
        new ExportField("INVOICE_SUBTOTAL", ExportKind.Money, "InvoiceSubtotal", "Subtotal de la factura", "Invoice subtotal"),
        new ExportField("INVOICE_TAX", ExportKind.Money, "InvoiceTax", "Impuesto de la factura", "Invoice tax"),
        new ExportField("INVOICE_TOTAL", ExportKind.Money, "InvoiceTotal", "Total de la factura", "Invoice total"),
        new ExportField("RUN_NUMBER", ExportKind.Number, "RunNo", "Número de la corrida", "Run number"),
        new ExportField("PERIOD_START", ExportKind.Date, "PeriodStart", "Inicio del período", "Period start"),
        new ExportField("PERIOD_END", ExportKind.Date, "PeriodEnd", "Fin del período", "Period end"),
    };

    // ---- Remesa COD (Lote 86): una fila por cobro, con los datos de la remesa repetidos en cada una
    public static readonly IReadOnlyList<ExportField> CodRemittance = new[]
    {
        new ExportField("REMITTANCE_NUMBER", ExportKind.Text, "RemittanceNo", "Número de la remesa", "Remittance number"),
        new ExportField("PERIOD_START", ExportKind.Date, "PeriodStart", "Inicio del período", "Period start"),
        new ExportField("PERIOD_END", ExportKind.Date, "PeriodEnd", "Fin del período", "Period end"),
        new ExportField("CLIENT_CODE", ExportKind.Text, "CustomerCode", "Código del cliente", "Customer code"),
        new ExportField("CLIENT_NAME", ExportKind.Text, "Customer", "Cliente", "Customer"),
        new ExportField("CURRENCY", ExportKind.Text, "Currency", "Moneda", "Currency"),
        new ExportField("ORDER_NUMBER", ExportKind.Text, "OrderNo", "Número de orden", "Order number"),
        new ExportField("INVOICE_NUMBER", ExportKind.Text, "InvoiceNo", "Factura del cliente", "Client invoice"),
        new ExportField("COLLECTED_DATE", ExportKind.Date, "CollectedDate", "Fecha del cobro", "Collection date"),
        new ExportField("METHOD", ExportKind.Text, "Method", "Método de cobro", "Payment method"),
        new ExportField("CHECK_NUMBER", ExportKind.Text, "CheckNo", "Número del cheque", "Check number"),
        new ExportField("REFERENCE", ExportKind.Text, "Reference", "Referencia", "Reference"),
        new ExportField("AMOUNT_COLLECTED", ExportKind.Money, "Collected", "Monto cobrado", "Amount collected"),
        new ExportField("COMMISSION_PCT", ExportKind.Number, "CommissionPct", "Comisión (%)", "Commission (%)"),
        new ExportField("COMMISSION_AMOUNT", ExportKind.Money, "Commission", "Comisión", "Commission"),
        new ExportField("NET_AMOUNT", ExportKind.Money, "Net", "Neto a remitir", "Net to remit"),
        new ExportField("BATCH_GROSS", ExportKind.Money, "BatchGross", "Bruto de la remesa", "Batch gross"),
        new ExportField("BATCH_COMMISSION", ExportKind.Money, "BatchCommission", "Comisión de la remesa", "Batch commission"),
        new ExportField("BATCH_NET", ExportKind.Money, "BatchNet", "Neto de la remesa", "Batch net"),
    };

    // ---- Liquidación a choferes (Lote 95): una fila por línea de la liquidación, con los datos de la liquidación repetidos en cada una
    public static readonly IReadOnlyList<ExportField> DriverSettlement = new[]
    {
        new ExportField("SETTLEMENT_NUMBER", ExportKind.Text, "SettlementNo", "Número de la liquidación", "Settlement number"),
        new ExportField("PERIOD_START", ExportKind.Date, "PeriodStart", "Inicio del período", "Period start"),
        new ExportField("PERIOD_END", ExportKind.Date, "PeriodEnd", "Fin del período", "Period end"),
        new ExportField("DRIVER_CODE", ExportKind.Text, "DriverCode", "Código del chofer", "Driver code"),
        new ExportField("DRIVER_NAME", ExportKind.Text, "Driver", "Chofer", "Driver"),
        new ExportField("CURRENCY", ExportKind.Text, "Currency", "Moneda", "Currency"),
        new ExportField("LINE_NO", ExportKind.Number, "LineNo", "Número de línea", "Line number"),
        new ExportField("LINE_KIND", ExportKind.Text, "Kind", "Tipo de línea (entrega o viaje)", "Line kind (delivery or trip)"),
        new ExportField("SERVICE_DATE", ExportKind.Date, "ServiceDate", "Fecha del servicio", "Service date"),
        new ExportField("ORDER_NUMBER", ExportKind.Text, "OrderNo", "Número de orden", "Order number"),
        new ExportField("DESCRIPTION", ExportKind.Text, "Description", "Descripción", "Description"),
        new ExportField("ATTEMPTS", ExportKind.Number, "Attempts", "Intentos", "Attempts"),
        new ExportField("DELIVERY_AMOUNT", ExportKind.Money, "DeliveryPay", "Pago por la entrega", "Delivery pay"),
        new ExportField("ATTEMPTS_AMOUNT", ExportKind.Money, "AttemptsPay", "Pago por los intentos", "Attempts pay"),
        new ExportField("CALCULATED_AMOUNT", ExportKind.Money, "Calculated", "Monto calculado", "Calculated amount"),
        new ExportField("AMOUNT", ExportKind.Money, "Amount", "Monto a pagar", "Amount to pay"),
        new ExportField("ADJUSTMENT_NOTE", ExportKind.Text, "AdjustmentNote", "Motivo del ajuste", "Adjustment note"),
        new ExportField("PAYOUT_FORMULA", ExportKind.Text, "Formula", "Fórmula de pago", "Payout formula"),
        new ExportField("RUN_TOTAL", ExportKind.Money, "RunTotal", "Total de la liquidación", "Settlement total"),
    };

    public static IReadOnlyList<ExportField> Of(string scope) => scope == ExportCodes.ScopeBilling ? Billing : scope == ExportCodes.ScopeCodRemittance ? CodRemittance : scope == ExportCodes.ScopeDriverSettlement ? DriverSettlement : Array.Empty<ExportField>();
}

/// <summary>Reglas puras de las plantillas (sin EF; ExportTemplateService las traduce a 400/404/409). Los mensajes son públicos: el manual y la FAQ los citan.</summary>
public static class ExportRules
{
    public const int MaxNameLength = 80;
    public const int MaxHeaderLength = 60;
    public const int MaxColumns = 40;

    public const string ScopeInvalid = "El alcance de la plantilla no existe.";
    public const string NameRequired = "Escriba el nombre de la plantilla.";
    public const string NameTooLong = "El nombre no puede pasar de 80 caracteres.";
    public const string NameTaken = "Ya existe una plantilla con ese nombre.";
    public const string FormatInvalid = "El formato debe ser CSV o TXT.";
    public const string DelimiterInvalid = "El delimitador debe ser coma, punto y coma, tabulador o barra vertical.";
    public const string DateFormatInvalid = "El formato de fecha debe ser YYYY-MM-DD, MM/DD/YYYY o DD/MM/YYYY.";
    public const string ColumnsRequired = "Elija al menos una columna.";
    public const string TooManyColumns = "La plantilla no puede tener más de 40 columnas.";
    public const string FieldInvalid = "Una de las columnas no es un campo exportable.";
    public const string FieldRepeated = "Una columna se repite en la plantilla.";
    public const string HeaderTooLong = "El encabezado de una columna no puede pasar de 60 caracteres.";
    public const string TemplateNotFound = "Plantilla de exportación no encontrada.";
    public const string LastTemplate = "No se puede desactivar la única plantilla del alcance: deje al menos una.";
    public const string ExportNeedsApproved = "Solo se exporta una corrida aprobada o exportada.";
    public const string TemplateScopeMismatch = "La plantilla no es de facturación.";

    /// <summary>El nombre normalizado para compararlo: sin espacios de más, sin mayúsculas ni acentos.</summary>
    public static string NormalizeName(string name)
    {
        var d = name.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(d.Length);
        foreach (var c in d)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return string.Join(' ', sb.ToString().ToUpperInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>Texto del delimitador para mostrarlo («tabulador» en vez del carácter).</summary>
    public static string DelimiterName(string d) => d == "\t" ? "tab" : d;

    /// <summary>Las columnas por omisión de una plantilla nueva: todas las del alcance.</summary>
    public static IReadOnlyList<ExportColumn> DefaultColumns(string scope) => ExportFields.Of(scope).Select(f => new ExportColumn(f.Code)).ToList();

    public static string SerializeColumns(IEnumerable<ExportColumn> cols) => JsonSerializer.Serialize(cols.Select(c => new { field = c.Field, header = string.IsNullOrWhiteSpace(c.Header) ? null : c.Header.Trim() }), ColumnJsonOptions);

    public static IReadOnlyList<ExportColumn> ParseColumns(string json)
    {
        try
        {
            var raw = JsonSerializer.Deserialize<List<ColumnDto>>(json, ColumnJsonOptions) ?? new List<ColumnDto>();
            return raw.Where(c => !string.IsNullOrWhiteSpace(c.Field)).Select(c => new ExportColumn(c.Field!, c.Header)).ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<ExportColumn>();
        }
    }

    private sealed record ColumnDto(string? Field, string? Header);

    private static readonly JsonSerializerOptions ColumnJsonOptions = new() { PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
}

/// <summary>El resultado de armar un archivo.</summary>
public sealed record ExportFile(string FileName, string ContentType, string Text, int RowCount);

/// <summary>
/// Arma el archivo de una plantilla con las filas dadas (cada fila: código de campo → valor: string, decimal, int, DateOnly o null). Determinista: las mismas filas y la misma plantilla dan el
/// mismo texto (la exportación es reproducible). CSV: cita con comillas lo que lleva el delimitador, comillas o saltos de línea. TXT: sin comillas; el delimitador y los saltos de línea dentro de un
/// valor se cambian por un espacio. Fin de línea CRLF.
/// </summary>
public static class ExportEngine
{
    public static string FormatDate(DateOnly d, string dateFormat) => dateFormat switch
    {
        ExportCodes.DateUs => d.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture),
        ExportCodes.DateEu => d.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        _ => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
    };

    /// <summary>Dinero con 2 decimales y punto; números con hasta 4 decimales sin ceros de sobra (mínimo 2 cuando llevan parte decimal).</summary>
    public static string FormatValue(ExportField field, object? value, string dateFormat)
    {
        if (value is null) return string.Empty;
        return value switch
        {
            DateOnly d => FormatDate(d, dateFormat),
            DateTime dt => FormatDate(DateOnly.FromDateTime(dt), dateFormat),
            decimal m when field.Kind == ExportKind.Money => Math.Round(m, 2, MidpointRounding.AwayFromZero).ToString("0.00", CultureInfo.InvariantCulture),
            decimal m => Number(m),
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            bool b => b ? "1" : "0",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    private static string Number(decimal m)
    {
        var s = Math.Round(m, 4, MidpointRounding.AwayFromZero).ToString("0.####", CultureInfo.InvariantCulture);
        return s;
    }

    private static string Escape(string text, string format, string delimiter)
    {
        if (format == ExportCodes.FormatTxt)
            return text.Replace(delimiter, " ").Replace("\r", " ").Replace("\n", " ");
        var needs = text.Contains(delimiter, StringComparison.Ordinal) || text.Contains('"') || text.Contains('\r') || text.Contains('\n');
        return needs ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
    }

    public static ExportFile Render(string scope, string format, string delimiter, string dateFormat, bool includeHeader, IReadOnlyList<ExportColumn> columns,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, string fileBaseName)
    {
        var fields = ExportFields.Of(scope).ToDictionary(f => f.Code);
        var cols = columns.Where(c => fields.ContainsKey(c.Field)).ToList();
        var sb = new StringBuilder();
        if (includeHeader)
            sb.Append(string.Join(delimiter, cols.Select(c => Escape(string.IsNullOrWhiteSpace(c.Header) ? fields[c.Field].DefaultHeader : c.Header!, format, delimiter)))).Append("\r\n");
        foreach (var row in rows)
            sb.Append(string.Join(delimiter, cols.Select(c => Escape(FormatValue(fields[c.Field], row.TryGetValue(c.Field, out var v) ? v : null, dateFormat), format, delimiter)))).Append("\r\n");
        var ext = format == ExportCodes.FormatTxt ? "txt" : "csv";
        return new ExportFile($"{fileBaseName}.{ext}", format == ExportCodes.FormatTxt ? "text/plain; charset=utf-8" : "text/csv; charset=utf-8", sb.ToString(), rows.Count);
    }
}
