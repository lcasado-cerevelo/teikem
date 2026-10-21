using Teikem.Domain.Common;

namespace Teikem.Domain.Cod;

/// <summary>Constantes del ciclo COD (Lote 84, P8): cobro contra entrega y su reconciliación (PWBack). Viven aquí para que el lote se pueda pasar solo a otra rama.</summary>
public static class CodCodes
{
    /// <summary>EntityType de la bitácora de cada cobro (sembrado en la sección 2026-10-17 (x) de update.sql).</summary>
    public const string CollectionEntityType = "COD_COLLECTION";
    /// <summary>Dominio de estatus del cobro individual (sembrado en el seed original).</summary>
    public const string CollectionStatusDomain = "CodCollectionStatus";
    public const string MethodDomain = "CodPaymentMethod";
}

/// <summary>Dominio CodCollectionStatus: COBRADO → CUADRADO → REMITIDO; ANULADO lateral. El cambio de estatus del cobro no tiene historial propio: el ciclo formal es el de la orden (ORDER_COD).</summary>
public static class CodCollectionStatuses
{
    public const string Collected = "COLLECTED";
    public const string Reconciled = "RECONCILED";
    public const string Remitted = "REMITTED";
    public const string Void = "VOID";
}

public static class CodMethods
{
    public const string Cash = "CASH";
    public const string Check = "CHECK";
    public const string CompanyCheck = "COMPANY_CHECK";
    public const string AthMovil = "ATH_MOVIL";
    public const string Card = "CARD";
    public static readonly string[] All = { Cash, Check, CompanyCheck, AthMovil, Card };
    /// <summary>Métodos que llevan un número de cheque.</summary>
    public static bool NeedsCheckNumber(string code) => code is Check or CompanyCheck;
}

/// <summary>Resultado de reconciliar un código (la factura o el número de la orden leído de una hoja escaneada o escrito a mano).</summary>
public static class CodReconcileResults
{
    /// <summary>Lo cobrado de la orden quedó cuadrado (con dryRun: se cuadraría).</summary>
    public const string Reconciled = "RECONCILED";
    public const string AlreadyReconciled = "ALREADY_RECONCILED";
    public const string NotFound = "NOT_FOUND";
    /// <summary>El código coincide con más de una orden: se resuelve a mano.</summary>
    public const string Ambiguous = "AMBIGUOUS";
    public const string NoCod = "NO_COD";
    /// <summary>La orden tiene COD pero todavía no se ha registrado ningún cobro.</summary>
    public const string NothingCollected = "NOTHING_COLLECTED";
    /// <summary>El mismo código ya venía antes en esta corrida (la misma hoja dos veces).</summary>
    public const string Duplicate = "DUPLICATE";
    // Lote 126 — procesamiento de la factura del cliente (orden SIN COD): el «second check» de la factura contra la orden entregada.
    /// <summary>La factura quedó procesada (cotejada) contra su orden entregada (con dryRun: se procesaría).</summary>
    public const string InvoiceChecked = "INVOICE_CHECKED";
    /// <summary>La orden existe pero todavía no se entregó: la factura se procesa cuando se entregue.</summary>
    public const string NotDelivered = "NOT_DELIVERED";
    /// <summary>La factura de esa orden ya se había procesado antes (otra corrida).</summary>
    public const string AlreadyProcessed = "ALREADY_PROCESSED";
    public static readonly string[] All = { Reconciled, AlreadyReconciled, NotFound, Ambiguous, NoCod, NothingCollected, Duplicate };
}

/// <summary>
/// Un cobro del COD hecho en la entrega (puede haber varios parciales por orden). COBRADO hasta que se cuadra contra la hoja (factura) que regresó de la calle; luego CUADRADO y, al remitirse al cliente,
/// REMITIDO. Hoy lo registra la web (`cod.collect`); la app de choferes lo hará con el POD (P7).
/// </summary>
[AuditEntity(CodCodes.CollectionEntityType)]
public class CodCollection : ITenantScoped
{
    public long CodCollectionId { get; set; }
    public int TenantId { get; set; }
    public int TransportOrderId { get; set; }
    public int? ProofOfDeliveryId { get; set; }
    public int? DriverId { get; set; }
    public int MethodLookupId { get; set; }
    public decimal AmountCollected { get; set; }
    public string? CheckNumber { get; set; }
    public string? Reference { get; set; }
    public DateTime CollectedAtUtc { get; set; }
    public int StatusCodeId { get; set; }
    public DateTime? ReconciledAtUtc { get; set; }
    public int? CodRemittanceBatchId { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    /// <summary>Quién lo registró y quién lo cuadró (columnas del Lote 84).</summary>
    public int? CollectedBy { get; set; }
    public int? ReconciledBy { get; set; }
    public int? ReconcileRunId { get; set; }
}

/// <summary>Una corrida de reconciliación: los códigos que se procesaron juntos (las hojas escaneadas de un día, o los que alguien escribió) y cuántos dieron cada resultado.</summary>
public class CodReconcileRun : ITenantScoped
{
    public int CodReconcileRunId { get; set; }
    public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    /// <summary>Texto libre: de dónde salieron los códigos (los nombres de los archivos, «Manual»…).</summary>
    public string? Source { get; set; }
    public int Total { get; set; }
    public int Reconciled { get; set; }
    public int Issues { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
    public List<CodReconcileItem> Items { get; set; } = new();
}

public class CodReconcileItem
{
    public int CodReconcileItemId { get; set; }
    public int CodReconcileRunId { get; set; }
    public string Code { get; set; } = string.Empty;
    /// <summary>De dónde se leyó el código (archivo y página).</summary>
    public string? Source { get; set; }
    public string Result { get; set; } = string.Empty;
    public int? TransportOrderId { get; set; }
    public decimal Amount { get; set; }
    public int Collections { get; set; }
}

/// <summary>Lote 126: de qué son las hojas que se suben, según quien las sube. AUTO = el sistema clasifica cada orden (con COD → hoja de COD; sin COD → factura) sin advertir; los demás advierten si lo que vino no es lo esperado.</summary>
public static class CodReconcileModes
{
    public const string Auto = "AUTO", Cod = "COD", Invoice = "INVOICE", Both = "BOTH";
    public static readonly string[] All = { Auto, Cod, Invoice, Both };
    /// <summary>Normaliza (vacío = AUTO); devuelve null si no es un modo válido.</summary>
    public static string? Normalize(string? mode)
    {
        var m = (mode ?? string.Empty).Trim().ToUpperInvariant();
        if (m.Length == 0) return Auto;
        return All.Contains(m) ? m : null;
    }
}

/// <summary>Lote 126: qué resultó ser cada hoja: de COD (la orden tiene COD) o de factura (la orden no tiene COD).</summary>
public static class CodReconcileKinds
{
    public const string Cod = "COD", Invoice = "INVOICE";
}

/// <summary>Lote 126: advertencias de una corrida según el modo elegido (informativas: lo leído se procesa igual).</summary>
public static class CodReconcileWarnings
{
    /// <summary>Dijo COD y vinieron hojas de órdenes sin COD (se cotejaron como facturas).</summary>
    public const string NotCodSheets = "NOT_COD_SHEETS";
    /// <summary>Dijo Facturas y vinieron hojas de órdenes con COD (se cuadraron como COD).</summary>
    public const string CodSheets = "COD_SHEETS";
    /// <summary>Dijo Ambos y no vino ninguna hoja de factura (órdenes sin COD).</summary>
    public const string NoInvoiceSheets = "NO_INVOICE_SHEETS";
    /// <summary>Dijo Ambos y no vino ninguna hoja de COD.</summary>
    public const string NoCodSheets = "NO_COD_SHEETS";
}

public static class CodRules
{
    public const string ModeInvalid = "El tipo de hojas debe ser AUTO, COD, INVOICE o BOTH.";
    public const int MaxCodeLength = 80;
    public const int MaxCodesPerRun = 2000;
    public const int MaxSourceLength = 300;
    public const int MaxNoteLength = 300;
    public const int MaxReferenceLength = 80;
    public const int MaxCheckNumberLength = 40;

    public const string OrderRequired = "Indique el envío.";
    public const string OrderHasNoCod = "El envío no tiene COD.";
    public const string OrderNotCollectable = "No se registra un cobro en un envío en borrador o cancelado.";
    public const string MethodRequired = "Indique el método de cobro.";
    public const string MethodInvalid = "El método de cobro no existe.";
    public const string AmountInvalid = "El monto debe ser mayor que cero (hasta 2 decimales).";
    public const string CheckNumberRequired = "Indique el número del cheque.";
    public const string CheckNumberTooLong = "El número del cheque no puede pasar de 40 caracteres.";
    public const string ReferenceTooLong = "La referencia no puede pasar de 80 caracteres.";
    public const string NoteTooLong = "La nota no puede pasar de 300 caracteres.";
    public const string CodesRequired = "Indique al menos un código.";
    public const string TooManyCodes = "Una corrida no puede tener más de 2000 códigos.";
    public const string CodeTooLong = "Un código no puede pasar de 80 caracteres.";
    public const string ClientInvalid = "El cliente no existe.";
    public const string SourceTooLong = "El origen no puede pasar de 300 caracteres.";

    public static string ExceedsPending(string orderNumber, decimal pending) => $"El cobro pasa de lo que falta por cobrar de {orderNumber}: {pending:0.00}.";

    /// <summary>Monto en dólares (o la moneda de la orden): mayor que cero y con a lo más 2 decimales.</summary>
    public static bool IsValidAmount(decimal v) => v > 0m && Math.Round(v, 2) == v;

    /// <summary>Lo que falta por cobrar: el COD de la orden menos lo cobrado (nunca negativo).</summary>
    public static decimal Pending(decimal codAmount, decimal collected) => Math.Max(0m, codAmount - collected);

    /// <summary>Estatus del COD de la orden según lo cobrado: nada = PENDING, parte = PARTIAL, todo = COLLECTED.</summary>
    public static string OrderStatusFor(decimal codAmount, decimal collected)
        => collected <= 0m ? "PENDING" : collected + 0.0001m >= codAmount ? "COLLECTED" : "PARTIAL";

    /// <summary>Normaliza un código leído de una hoja: sin espacios, sin guiones largos raros ni ceros de control.</summary>
    public static string NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return string.Empty;
        var chars = code.Trim().Where(c => !char.IsControl(c)).ToArray();
        return new string(chars).Trim();
    }
}
