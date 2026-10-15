using Teikem.Domain.Common;

namespace Teikem.Domain.Tax;

/// <summary>Constantes de impuestos (Lote 55). Viven aquí (y no en CatalogDomains) para que el lote se pueda pasar solo a otra rama.</summary>
public static class TaxCodes
{
    public const string RateEntityType = "TAX_RATE";
    public const string ExemptionEntityType = "TAX_EXEMPTION";
    public const string SettingEntityType = "TAX_SETTING";
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
    public const string NotTaxableCharge = "NOT_TAXABLE_CHARGE";
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
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
}
