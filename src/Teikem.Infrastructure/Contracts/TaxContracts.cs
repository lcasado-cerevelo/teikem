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

/// <summary>
/// Una exención. Status: VALID (vigente hoy), SCHEDULED (empieza después), EXPIRED (venció) o INACTIVE (dada de baja).
/// Lote 237 — Scope: ALL (todo el impuesto) o COMPONENTS (solo los componentes de ComponentCodes, p. ej. ["CITY"]; vacío con ALL).
/// Lote 248 — ValidFrom, ValidTo y CertificateNumber son los EFECTIVOS (los del documento que la respalda, si está vinculada). DocumentPublicId, DocumentType
/// (código), DocumentTypeLabel (en el idioma de la interfaz) y FilePublicId (el archivo, por GET /files/{id}) describen ese documento; MissingDocument = true
/// es una exención vieja «falta el documento» (sigue valiendo como antes, pero editarla o reactivarla exige vincular uno). Status DOCUMENT_REMOVED: el
/// documento que la respaldaba se quitó y ya no exime nada.
/// </summary>
public sealed record TaxExemptionDto(int Id, Guid PublicId, int ClientId, string ClientName, string ExemptionType, string CertificateNumber,
    DateOnly ValidFrom, DateOnly? ValidTo, string? Notes, bool IsActive, string Status, string Scope = "ALL", IReadOnlyList<string>? ComponentCodes = null,
    Guid? ClientPublicId = null, Guid? DocumentPublicId = null, string? DocumentType = null, string? DocumentTypeLabel = null, Guid? FilePublicId = null,
    bool MissingDocument = false);

/// <summary>
/// Lote 237 — Scope: ALL (por omisión) o COMPONENTS; con COMPONENTS, ComponentCodes lleva los códigos de componente exentos (al menos uno).
/// Lote 248 — ClientDocumentPublicId es OBLIGATORIO (el documento del cliente, de un tipo «de impuestos», con número y «hasta» de hoy en adelante). El número y
/// las fechas se toman del documento (CertificateNumber, ValidFrom y ValidTo de este cuerpo se ignoran); ExemptionType vacío = la etiqueta del tipo de documento.
/// </summary>
public sealed record TaxExemptionRequest(int? ClientId = null, string? ExemptionType = null, string? CertificateNumber = null, DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null, string? Notes = null, string? Scope = null, IReadOnlyList<string>? ComponentCodes = null, Guid? ClientDocumentPublicId = null);

/// <summary>
/// PATCH: lo que no viene no cambia; ClearValidTo = sin fecha final. Lote 237 — Scope y ComponentCodes (con ALL los componentes se quitan).
/// Lote 248 — en una exención vinculada, ValidFrom, ValidTo, ClearValidTo y CertificateNumber dan 400 (son los del documento); ClientDocumentPublicId la vincula
/// a otro documento (renovación). Una exención vieja sin documento solo se edita si el PATCH trae ClientDocumentPublicId (vincular).
/// </summary>
public sealed record TaxExemptionPatchRequest(string? ExemptionType = null, string? CertificateNumber = null, DateOnly? ValidFrom = null,
    DateOnly? ValidTo = null, bool? ClearValidTo = null, string? Notes = null, string? Scope = null, IReadOnlyList<string>? ComponentCodes = null,
    Guid? ClientDocumentPublicId = null);

/// <summary>Categoría de impuesto de un producto: TAXABLE, EXEMPT o ZERO_RATE.</summary>
public sealed record ProductTaxCategoryRequest(string? Category = null);

public sealed record ProductTaxCategoryDto(int ProductId, string Sku, string Name, string Category);

/// <summary>Una línea a calcular: con ProductId manda la categoría del producto; si no, la regla del tipo de cargo (ChargeType).</summary>
public sealed record TaxCalcLineRequest(string? Key = null, int? ProductId = null, string? ChargeType = null, decimal? Amount = null);

public sealed record TaxCalcRequest(int? ClientId = null, DateOnly? Date = null, IReadOnlyList<TaxCalcLineRequest>? Lines = null);

/// <summary>
/// Un componente de la tasa en el resultado. Lote 237 — si la exención del cliente lo cubre: Amount = 0, ExemptReason = EXEMPT_CLIENT_PARTIAL y
/// ExemptionCertificate = el certificado que lo exime (Percent sigue siendo el porcentaje del componente en la tasa).
/// </summary>
public sealed record TaxCalcComponentDto(string Code, string Name, decimal Percent, decimal Amount, string? ExemptReason = null, string? ExemptionCertificate = null);

/// <summary>Lote 248 — ExemptionPeriod: el nombre del período de exención por temporada que eximió la línea (Reason = EXEMPT_PRODUCT_PERIOD); si no, null.</summary>
public sealed record TaxCalcLineDto(string Key, decimal Amount, decimal Net, decimal TaxAmount, decimal Total, string Reason, bool IsTaxExempt,
    int? TaxRateId, decimal TaxRatePct, string? ExemptionCertificate, IReadOnlyList<TaxCalcComponentDto> Components, string? ExemptionPeriod = null);

/// <remarks>
/// Lote 237 — con una exención parcial del cliente, las líneas gravables salen con Reason = EXEMPT_CLIENT_PARTIAL, TaxRatePct = el porcentaje realmente
/// cobrado (la tasa sin los componentes exentos), IsTaxExempt = false y ExemptionCertificate = el certificado (o certificados) que aplicaron.
/// RatePercent sigue siendo el porcentaje completo de la tasa.
/// </remarks>
/// <summary>Resultado del cálculo: por línea y total, con la tasa usada, las reglas aplicadas y los avisos (por ejemplo, exención vencida).</summary>
public sealed record TaxCalcDto(DateOnly Date, string? RateCode, decimal RatePercent, bool PricesIncludeTax, string RoundingMode,
    IReadOnlyList<TaxCalcLineDto> Lines, decimal Subtotal, decimal TaxAmount, decimal Total, IReadOnlyList<TaxCalcComponentDto> Components,
    IReadOnlyList<string> Warnings);

// ======================================================================================================================
// Lote 248 — captura única de la exención desde la ficha del cliente (POST /api/v1/clients/{id}/tax-exemptions, tax.manage + módulo TAX).
// ======================================================================================================================

/// <summary>
/// Lote 248 — la exención con su certificado en un solo paso: con un archivo NUEVO (DocumentType, DocumentNumber, ValidFrom y ValidTo obligatorios; se crea el
/// documento del cliente y la exención en una sola transacción) o con un documento YA SUBIDO (ClientDocumentPublicId; número y fechas salen del documento). Nunca
/// las dos cosas. Scope/ComponentCodes como en <see cref="TaxExemptionRequest"/>; ExemptionType vacío = la etiqueta del tipo de documento.
/// </summary>
public sealed record ClientTaxExemptionCaptureRequest(Guid? ClientDocumentPublicId = null, string? DocumentType = null, string? DocumentNumber = null,
    DateOnly? ValidFrom = null, DateOnly? ValidTo = null, string? Scope = null, IReadOnlyList<string>? ComponentCodes = null, string? Notes = null,
    string? ExemptionType = null);

/// <summary>Lote 248 — la exención creada y el documento que la respalda (DocumentPublicId) con su archivo (FilePublicId, por GET /files/{id}).</summary>
public sealed record ClientTaxExemptionCaptureDto(TaxExemptionDto Exemption, Guid DocumentPublicId, Guid FilePublicId);
