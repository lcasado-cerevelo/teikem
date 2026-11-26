using Teikem.Domain.Common;

namespace Teikem.Domain.Tax;

/// <summary>Constantes de impuestos (Lote 55). Viven aquí (y no en CatalogDomains) para que el lote se pueda pasar solo a otra rama.</summary>
public static class TaxCodes
{
    public const string RateEntityType = "TAX_RATE";
    public const string ExemptionEntityType = "TAX_EXEMPTION";
    public const string SettingEntityType = "TAX_SETTING";
    /// <summary>Lote 248 — EntityType de la bitácora del período de exención por temporada (sembrado en la sección del Lote 248 de update.sql).</summary>
    public const string ExemptionPeriodEntityType = "TAX_EXEMPTION_PERIOD";
    /// <summary>Dominio de catálogo (LookupCode) con la categoría de impuesto de un producto.</summary>
    public const string CategoryDomain = "TaxCategory";
}

/// <summary>Categoría de impuesto de un producto. Sin categoría (NULL) = TAXABLE.</summary>
public static class TaxCategories
{
    public const string Taxable = "TAXABLE";
    public const string Exempt = "EXEMPT";
    public const string ZeroRate = "ZERO_RATE";
    public static readonly string[] All = { Taxable, Exempt, ZeroRate };
}

public static class TaxRoundingModes
{
    /// <summary>Se redondea el impuesto de cada línea a 2 decimales; el total es la suma de las líneas.</summary>
    public const string Line = "LINE";
    /// <summary>Se suma sin redondear y se redondea una sola vez el impuesto del documento.</summary>
    public const string Document = "DOCUMENT";
    public static readonly string[] All = { Line, Document };
}

/// <summary>Por qué una línea salió con ese impuesto (o sin él).</summary>
public static class TaxReasons
{
    public const string Taxable = "TAXABLE";
    public const string ZeroRate = "ZERO_RATE";
    public const string ExemptProduct = "EXEMPT_PRODUCT";
    public const string ExemptClient = "EXEMPT_CLIENT";
    /// <summary>Lote 237 — el cliente tiene exenta solo una parte de la tasa (p. ej. el componente municipal): la línea paga el resto.</summary>
    public const string ExemptClientPartial = "EXEMPT_CLIENT_PARTIAL";
    public const string NotTaxableCharge = "NOT_TAXABLE_CHARGE";
    /// <summary>Lote 248 — el producto está dentro de un período de exención por temporada (TaxExemptionPeriod): la línea sale sin impuesto.</summary>
    public const string ExemptProductPeriod = "EXEMPT_PRODUCT_PERIOD";
    /// <summary>
    /// Lote 248 (declarado) / Lote 249 (dueño) — IVU MANUAL de una línea de venta: lo escribe SOLO una modificación (sales.tax + motivo) y se limpia al quitar el IVU
    /// manual (vuelve el cálculo normal, con la exención del cliente si aplica). Solo afecta esa línea. Ver SalesChangeRules.ManualTax.
    /// </summary>
    public const string ManualOverride = "MANUAL_OVERRIDE";
}

/// <summary>Lote 237 — alcance de la exención de un cliente: todo el impuesto o solo algunos componentes de la tasa (p. ej. el municipal).</summary>
public static class TaxExemptionScopes
{
    public const string All = "ALL";
    public const string Components = "COMPONENTS";
    public static readonly string[] Values = { All, Components };
    /// <summary>Tope de componentes que una exención puede nombrar (cabe en la columna ComponentCodes).</summary>
    public const int MaxComponents = 10;
}

/// <summary>Lote 248 — alcance de un período de exención por temporada: todos los productos o los escogidos (categorías, con sus subcategorías, y productos).</summary>
public static class TaxExemptionPeriodScopes
{
    public const string All = "ALL";
    public const string Selected = "SELECTED";
    public static readonly string[] Values = { All, Selected };
}

/// <summary>
/// Lote 55 — una tasa de impuesto de la compañía (p. ej. IVU). NO se edita: para cambiarla se cierra su vigencia (EffectiveTo) y se crea otra
/// con el mismo Code; las líneas ya emitidas conservan la tasa con que se calcularon. Percent = suma de los componentes (estatal, municipal…).
/// Nunca DELETE. Para un mismo Code no hay vigencias que se traslapen.
/// </summary>
[AuditEntity(TaxCodes.RateEntityType)]
public class TaxRate : ITenantScoped, IAuditStamped
{
    public int TaxRateId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Percent { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public DateOnly? EffectiveTo { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    public List<TaxRateComponent> Components { get; set; } = new();
}

/// <summary>Parte de una tasa (estatal, municipal…) para poder reportarlas por separado. La suma de los componentes es el porcentaje de la tasa.</summary>
public class TaxRateComponent : ITenantScoped
{
    public int TaxRateComponentId { get; set; }
    public int TenantId { get; set; }
    public int TaxRateId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public decimal Percent { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>
/// Lote 55 — reglas generales de impuestos de la compañía (una fila por compañía, se crea sola con valores por omisión):
/// ¿los precios ya incluyen el impuesto?, redondeo por línea o por documento y el código de la tasa que se usa por omisión.
/// </summary>
[AuditEntity(TaxCodes.SettingEntityType)]
public class TenantTaxSetting : ITenantScoped, IAuditStamped
{
    public int TenantTaxSettingId { get; set; }
    public int TenantId { get; set; }
    public bool PricesIncludeTax { get; set; }
    public string RoundingMode { get; set; } = TaxRoundingModes.Line;
    public string? DefaultRateCode { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
}

/// <summary>Si un tipo de cargo (flete, manejo, recargo…) paga impuesto en esta compañía. Sin fila = no gravable. PRODUCT_SALE lo decide el producto.</summary>
public class TaxChargeRule : ITenantScoped
{
    public int TaxChargeRuleId { get; set; }
    public int TenantId { get; set; }
    public int ChargeTypeLookupId { get; set; }
    public bool IsTaxable { get; set; }
}

/// <summary>
/// Lote 55 — exención de impuesto de un cliente (certificado). Una venta a un cliente con exención VIGENTE sale sin impuesto, con el certificado
/// anotado; con la exención vencida se avisa y se cobra el impuesto. Nunca DELETE: se da de baja (IsActive = 0).
/// Lote 237 — Scope: ALL = exime todo el impuesto (como siempre); COMPONENTS = exime solo los componentes de ComponentCodes (códigos de componente de la
/// tasa, en mayúsculas, ordenados y separados por coma: «CITY» o «CITY,STATE»); el resto de la tasa se cobra.
/// </summary>
[AuditEntity(TaxCodes.ExemptionEntityType)]
public class ClientTaxExemption : ITenantScoped, IAuditStamped
{
    public int ClientTaxExemptionId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int ClientId { get; set; }
    public string ExemptionType { get; set; } = string.Empty;
    public string CertificateNumber { get; set; } = string.Empty;
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public string Scope { get; set; } = TaxExemptionScopes.All;
    public string? ComponentCodes { get; set; }
    /// <summary>
    /// Lote 248 — el documento del cliente que respalda la exención (FK compuesta (ClientDocumentId, TenantId, ClientId) en el SQL). Vinculada, la vigencia y el número
    /// son los del DOCUMENTO (ClientExemptionBacking); ValidFrom/ValidTo/CertificateNumber de esta fila son una copia sincronizada para los listados. NULL = exención
    /// vieja «falta el documento»: sigue valiendo como antes, pero editarla o reactivarla exige vincular un documento. Un documento respalda a lo más una exención activa.
    /// </summary>
    public int? ClientDocumentId { get; set; }
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
}

/// <summary>
/// Lote 248 — exención de producto por temporada (p. ej. los artículos escolares en agosto por orden administrativa): mientras rige (ValidFrom..ValidTo, bordes incluidos)
/// las líneas de PRODUCTO gravables o de tasa cero que cubre salen sin impuesto con el motivo EXEMPT_PRODUCT_PERIOD (los cargos no se tocan). Scope ALL = todos los productos;
/// SELECTED = los productos y las categorías (con sus subcategorías) escogidos. Programado se edita todo; ya empezado solo se mueve la fecha final o se da de baja.
/// Sin StatusCode: el estado (programado, vigente, terminado, inactivo) se deriva de la fecha. Nunca DELETE.
/// </summary>
[AuditEntity(TaxCodes.ExemptionPeriodEntityType)]
public class TaxExemptionPeriod : ITenantScoped, ISoftDeletable, IAuditStamped
{
    public int TaxExemptionPeriodId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Por qué se exime (p. ej. la orden administrativa que lo decreta). Obligatorio.</summary>
    public string Reason { get; set; } = string.Empty;
    public DateOnly ValidFrom { get; set; }
    public DateOnly ValidTo { get; set; }
    public string Scope { get; set; } = TaxExemptionPeriodScopes.All;
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }
    public List<TaxExemptionPeriodProduct> Products { get; set; } = new();
    public List<TaxExemptionPeriodCategory> Categories { get; set; } = new();
}

/// <summary>Lote 248 — un producto escogido de un período SELECTED (FK compuestas con la compañía en el SQL). Quitarlo = IsActive 0; volver a escogerlo lo reactiva.</summary>
[AuditEntity(TaxCodes.ExemptionPeriodEntityType)]
public class TaxExemptionPeriodProduct : ITenantScoped, ISoftDeletable
{
    public int TaxExemptionPeriodProductId { get; set; }
    public int TenantId { get; set; }
    public int TaxExemptionPeriodId { get; set; }
    public int ProductId { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
}

/// <summary>Lote 248 — una categoría de productos escogida de un período SELECTED (incluye sus subcategorías). Quitarla = IsActive 0; volver a escogerla la reactiva.</summary>
[AuditEntity(TaxCodes.ExemptionPeriodEntityType)]
public class TaxExemptionPeriodCategory : ITenantScoped, ISoftDeletable
{
    public int TaxExemptionPeriodCategoryId { get; set; }
    public int TenantId { get; set; }
    public int TaxExemptionPeriodId { get; set; }
    public int ProductCategoryId { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
}
