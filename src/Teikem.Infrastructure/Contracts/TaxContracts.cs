namespace Teikem.Infrastructure.Contracts;

// ======================================================================================================================
// Lote 55 — impuestos (IVU). Permiso tax.manage, módulo TAX. Las tasas no se editan: se cierra su vigencia y se crea otra.
// ======================================================================================================================

public sealed record TaxComponentDto(string Code, string Name, decimal Percent);

/// <summary>Una tasa. Status: ACTIVE (vigente hoy), SCHEDULED (empieza después) o CLOSED (ya terminó).</summary>
public sealed record TaxRateDto(int Id, Guid PublicId, string Code, string Name, decimal Percent, DateOnly EffectiveFrom, DateOnly? EffectiveTo,
    string Status, IReadOnlyList<TaxComponentDto> Components);

/// <summary>Tasa nueva. El porcentaje es la suma de los componentes. EffectiveTo vacío = sin fecha final.</summary>
public sealed record TaxRateRequest(string? Code = null, string? Name = null, DateOnly? EffectiveFrom = null, DateOnly? EffectiveTo = null,
    IReadOnlyList<TaxComponentDto>? Components = null);

/// <summary>Cierra la vigencia de una tasa (EffectiveTo = último día en que rige).</summary>
public sealed record TaxRateCloseRequest(DateOnly? EffectiveTo = null);

public sealed record TaxChargeRuleDto(string ChargeType, string Label, bool IsTaxable);

public sealed record TaxSettingsDto(bool PricesIncludeTax, string RoundingMode, string? DefaultRateCode, IReadOnlyList<TaxChargeRuleDto> ChargeRules);

public sealed record TaxChargeRuleRequest(string? ChargeType = null, bool? IsTaxable = null);

/// <summary>PATCH de las reglas: lo que no viene no cambia. ClearDefaultRate = quita la tasa por omisión.</summary>
public sealed record TaxSettingsRequest(bool? PricesIncludeTax = null, string? RoundingMode = null, string? DefaultRateCode = null, bool? ClearDefaultRate = null,
    IReadOnlyList<TaxChargeRuleRequest>? ChargeRules = null);

/// <summary>Una exención. Status: VALID (vigente hoy), SCHEDULED (empieza después), EXPIRED (venció) o INACTIVE (dada de baja).</summary>
public sealed record TaxExemptionDto(int Id, Guid PublicId, int ClientId, string ClientName, string ExemptionType, string CertificateNumber,
    DateOnly ValidFrom, DateOnly? ValidTo, string? Notes, bool IsActive, string Status);

public sealed record TaxExemptionRequest(int? ClientId = null, string? ExemptionType = null, string? CertificateNumber = null, DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null, string? Notes = null);

/// <summary>PATCH: lo que no viene no cambia; ClearValidTo = sin fecha final.</summary>
public sealed record TaxExemptionPatchRequest(string? ExemptionType = null, string? CertificateNumber = null, DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null, bool? ClearValidTo = null, string? Notes = null);

/// <summary>Categoría de impuesto de un producto: TAXABLE, EXEMPT o ZERO_RATE.</summary>
public sealed record ProductTaxCategoryRequest(string? Category = null);

public sealed record ProductTaxCategoryDto(int ProductId, string Sku, string Name, string Category);

/// <summary>Una línea a calcular: con ProductId manda la categoría del producto; si no, la regla del tipo de cargo (ChargeType).</summary>
public sealed record TaxCalcLineRequest(string? Key = null, int? ProductId = null, string? ChargeType = null, decimal? Amount = null);

public sealed record TaxCalcRequest(int? ClientId = null, DateOnly? Date = null, IReadOnlyList<TaxCalcLineRequest>? Lines = null);

public sealed record TaxCalcComponentDto(string Code, string Name, decimal Percent, decimal Amount);

public sealed record TaxCalcLineDto(string Key, decimal Amount, decimal Net, decimal TaxAmount, decimal Total, string Reason, bool IsTaxExempt,
    int? TaxRateId, decimal TaxRatePct, string? ExemptionCertificate, IReadOnlyList<TaxCalcComponentDto> Components);

/// <summary>Resultado del cálculo: por línea y total, con la tasa usada, las reglas aplicadas y los avisos (por ejemplo, exención vencida).</summary>
public sealed record TaxCalcDto(DateOnly Date, string? RateCode, decimal RatePercent, bool PricesIncludeTax, string RoundingMode,
    IReadOnlyList<TaxCalcLineDto> Lines, decimal Subtotal, decimal TaxAmount, decimal Total, IReadOnlyList<TaxCalcComponentDto> Components,
    IReadOnlyList<string> Warnings);
