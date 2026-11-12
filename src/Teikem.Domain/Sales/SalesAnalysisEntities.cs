using Teikem.Domain.Common;

namespace Teikem.Domain.Sales;

/// <summary>
/// Lote 241 — constantes del «Análisis de ventas» (rutas, representantes, cuotas, líneas e historial importado de QuickBooks). Viven aquí (y no en los catálogos
/// compartidos) para que el lote se pueda pasar solo a otra rama. Los EntityType se siembran en la sección «2026-11-12 — Lote 241» de logistica-db-update.sql.
/// </summary>
public static class SalesAnalysisCodes
{
    public const string RouteEntityType = "SALES_ROUTE";
    public const string RepEntityType = "SALES_REP";
    public const string QuotaEntityType = "SALES_QUOTA";
    public const string LineEntityType = "PRODUCT_LINE";
    /// <summary>Ajustes del análisis, lotes importados y enlaces a mano de nombres del historial.</summary>
    public const string AnalysisEntityType = "SALES_ANALYSIS";
    /// <summary>Fuente de datos de Vistas, Indicadores y Gráficos: «Líneas de venta para análisis» (historial importado + facturas de Teikem).</summary>
    public const string DataSourceKey = "SALES_ANALYSIS_LINE";

    /// <summary>Clase de una línea de venta.</summary>
    public const string ClassProduct = "PRODUCT";
    public const string ClassDiscount = "DISCOUNT";
    public const string ClassShipping = "SHIPPING";
    public const string ClassOther = "OTHER";
    public static readonly string[] Classes = { ClassProduct, ClassDiscount, ClassShipping, ClassOther };

    /// <summary>Tipo de archivo importado.</summary>
    public const string KindSales = "SALES";
    public const string KindPurchases = "PURCHASES";

    /// <summary>Tipo de enlace a mano de un nombre crudo del historial.</summary>
    public const string AliasClient = "CLIENT";
    public const string AliasProduct = "PRODUCT";

    /// <summary>De dónde sale una línea del análisis.</summary>
    public const string SourceHistory = "HISTORY";
    public const string SourceInvoice = "INVOICE";
    public const string SourceCredit = "CREDIT";

    /// <summary>Origen de una importación.</summary>
    public const string ImportCli = "CLI";
    public const string ImportApi = "API";
}

/// <summary>Lote 241 — ruta de venta (Cadenas, Norte, Metro…). «Cadenas» es una ruta más. Nombre único entre las activas de la compañía.</summary>
[AuditEntity(SalesAnalysisCodes.RouteEntityType)]
public class SalesRoute : ITenantScoped, ISoftDeletable
{
    public int SalesRouteId { get; set; }
    public int TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; } = 100;
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Lote 241 — representante de ventas: nombre, ruta (opcional) y, si es un usuario de Teikem, su usuario (opcional). La venta de un cliente es del representante que tiene
/// asignado (Client.SalesRepId); el historial importado no trae representante y se atribuye al actual del cliente.
/// </summary>
[AuditEntity(SalesAnalysisCodes.RepEntityType)]
public class SalesRep : ITenantScoped, ISoftDeletable
{
    public int SalesRepId { get; set; }
    public int TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int? SalesRouteId { get; set; }
    public int? UserId { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Lote 241 — cuota mensual de un representante en dólares. Month = primer día del mes. Un mes sin cuota propia vale la del mes anterior más reciente
/// (<see cref="SalesAnalysisRules.EffectiveQuota"/>). Quitar la de un mes = IsActive 0 (vuelve a valer la anterior).
/// </summary>
[AuditEntity(SalesAnalysisCodes.QuotaEntityType)]
public class SalesQuota : ITenantScoped, ISoftDeletable
{
    public int SalesQuotaId { get; set; }
    public int TenantId { get; set; }
    public int SalesRepId { get; set; }
    public DateOnly Month { get; set; }
    public decimal Amount { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Lote 241 — «distribuidor o línea» (p. ej. Global Imports agrupa A.Belle, American Red Cross, Global y Yardley). Cada marca pertenece a una línea.</summary>
[AuditEntity(SalesAnalysisCodes.LineEntityType)]
public class ProductLine : ITenantScoped, ISoftDeletable
{
    public int ProductLineId { get; set; }
    public int TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int SortOrder { get; set; } = 100;
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Lote 241 — una marca (Product.Brand, texto libre) dentro de una línea. Única por compañía (una marca pertenece a UNA línea; sin distinguir mayúsculas). Una marca
/// sin fila aquí es su propia línea.
/// </summary>
[AuditEntity(SalesAnalysisCodes.LineEntityType)]
public class ProductLineBrand : ITenantScoped
{
    public int ProductLineBrandId { get; set; }
    public int TenantId { get; set; }
    public int ProductLineId { get; set; }
    public string Brand { get; set; } = string.Empty;
}

/// <summary>
/// Lote 241 — ajustes del análisis por compañía (una fila): fecha de corte (el análisis suma el historial importado ANTES de ella y las facturas de Teikem desde ella;
/// NULL = todo cuenta y se avisa si se traslapan), días de entrega y nivel de servicio (z) del punto de reorden sugerido.
/// </summary>
[AuditEntity(SalesAnalysisCodes.AnalysisEntityType)]
public class SalesAnalysisSetting : ITenantScoped
{
    public int TenantId { get; set; }
    public DateOnly? CutoffDate { get; set; }
    public int LeadTimeDays { get; set; } = SalesAnalysisRules.DefaultLeadTimeDays;
    public decimal ServiceLevelZ { get; set; } = SalesAnalysisRules.DefaultServiceLevelZ;
    [NotAudited] public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? UpdatedBy { get; set; }
}

/// <summary>
/// Lote 241 — un archivo importado (SalesByItemDetail o PurchasesByItemDetail de QuickBooks). Reimportar el MISMO archivo (misma huella) reemplaza su lote; un archivo
/// distinto del mismo tipo reemplaza las líneas que caen en su rango de fechas (no se duplica nada). Un lote reemplazado queda IsActive 0 con la fecha.
/// </summary>
[AuditEntity(SalesAnalysisCodes.AnalysisEntityType)]
public class SalesHistoryBatch : ITenantScoped, ISoftDeletable
{
    public int SalesHistoryBatchId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string Kind { get; set; } = SalesAnalysisCodes.KindSales;
    public string FileName { get; set; } = string.Empty;
    public string FileHash { get; set; } = string.Empty;
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public int LineCount { get; set; }
    public decimal TotalAmount { get; set; }
    public string Source { get; set; } = SalesAnalysisCodes.ImportCli;
    public DateTime ImportedAtUtc { get; set; } = DateTime.UtcNow;
    public int? ImportedBy { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? ReplacedAtUtc { get; set; }
}

/// <summary>
/// Lote 241 — una línea de venta del historial de QuickBooks TAL CUAL (sin auditoría: es copia de un archivo externo; reimportar la reemplaza). ClientId / ProductId =
/// lo que emparejó (por código de QuickBooks, nombre normalizado, SKU normalizado o enlace a mano); NULL = sin emparejar (se reporta y se puede enlazar a mano).
/// </summary>
public class SalesHistoryLine : ITenantScoped
{
    public long SalesHistoryLineId { get; set; }
    public int TenantId { get; set; }
    public int SalesHistoryBatchId { get; set; }
    public int SourceLine { get; set; }
    public DateOnly TxnDate { get; set; }
    public string TxnType { get; set; } = string.Empty;
    public string? DocNumber { get; set; }
    public string? Memo { get; set; }
    public string ClientNameRaw { get; set; } = string.Empty;
    public int? ClientId { get; set; }
    public string? Section { get; set; }
    public string ItemRaw { get; set; } = string.Empty;
    public string ItemSku { get; set; } = string.Empty;
    public int? ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal? UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public string LineClass { get; set; } = SalesAnalysisCodes.ClassProduct;
}

/// <summary>Lote 241 — una línea de compra del historial de QuickBooks (Bill / Item Receipt / Credit) TAL CUAL; las de cantidad 0 y los duplicados no se guardan.</summary>
public class PurchaseHistoryLine : ITenantScoped
{
    public long PurchaseHistoryLineId { get; set; }
    public int TenantId { get; set; }
    public int SalesHistoryBatchId { get; set; }
    public int SourceLine { get; set; }
    public DateOnly TxnDate { get; set; }
    public string TxnType { get; set; } = string.Empty;
    public string? DocNumber { get; set; }
    public string SupplierNameRaw { get; set; } = string.Empty;
    public string ItemRaw { get; set; } = string.Empty;
    public string ItemSku { get; set; } = string.Empty;
    public int? ProductId { get; set; }
    public decimal Quantity { get; set; }
    public decimal? UnitCost { get; set; }
    public decimal Amount { get; set; }
}

/// <summary>
/// Lote 241 — enlace a mano de un nombre crudo del historial (cliente o ítem) a un cliente o producto de Teikem. Sirve para re-enlazar las líneas ya importadas y para que
/// las próximas importaciones emparejen solas. RawKey = el nombre normalizado (<see cref="SalesAnalysisRules.NameKey"/>) o la clave del SKU.
/// </summary>
[AuditEntity(SalesAnalysisCodes.AnalysisEntityType)]
public class SalesHistoryAlias : ITenantScoped
{
    public int SalesHistoryAliasId { get; set; }
    public int TenantId { get; set; }
    public string Kind { get; set; } = SalesAnalysisCodes.AliasClient;
    public string RawKey { get; set; } = string.Empty;
    public int? ClientId { get; set; }
    public int? ProductId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
}
