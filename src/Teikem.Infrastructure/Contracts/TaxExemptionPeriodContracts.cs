namespace Teikem.Infrastructure.Contracts;

// ======================================================================================================================
// Lote 248 — exención de producto por temporada (TaxExemptionPeriod). Permiso tax.manage, módulo TAX. Ruta /api/v1/tax/exemption-periods.
// ======================================================================================================================

/// <summary>Un producto escogido de un período SELECTED.</summary>
public sealed record TaxExemptionPeriodProductDto(Guid ProductPublicId, int ProductId, string Sku, string Name);

/// <summary>Una categoría de productos escogida de un período SELECTED (incluye sus subcategorías).</summary>
public sealed record TaxExemptionPeriodCategoryDto(int CategoryId, string Name);

/// <summary>
/// Un período. State (derivado de la fecha, sin StatusCode): SCHEDULED (empieza después: se edita todo), ACTIVE (vigente hoy: solo se mueve la fecha final o se
/// da de baja), ENDED (ya terminó) o INACTIVE (dado de baja). Scope: ALL (todos los productos) o SELECTED (Products y Categories). RowVersion (base64) va en el
/// PATCH para el control de concurrencia (409 si cambió).
/// </summary>
public sealed record TaxExemptionPeriodDto(Guid PublicId, string Name, string Reason, DateOnly ValidFrom, DateOnly ValidTo, string Scope, bool IsActive, string State,
    IReadOnlyList<TaxExemptionPeriodProductDto> Products, IReadOnlyList<TaxExemptionPeriodCategoryDto> Categories, string? RowVersion);

/// <summary>Período nuevo. Todo obligatorio salvo la selección, que con SELECTED lleva al menos un producto (por PublicId) o una categoría (por id).</summary>
public sealed record TaxExemptionPeriodRequest(string? Name = null, string? Reason = null, DateOnly? ValidFrom = null, DateOnly? ValidTo = null, string? Scope = null,
    IReadOnlyList<Guid>? ProductPublicIds = null, IReadOnlyList<int>? CategoryIds = null);

/// <summary>
/// PATCH: lo que no viene no cambia. ProductPublicIds / CategoryIds REEMPLAZAN la selección (lo quitado queda con IsActive = 0, lo que vuelve se reactiva).
/// Ya empezado, solo cambia ValidTo (de hoy en adelante); cualquier otro cambio da 409.
/// </summary>
public sealed record TaxExemptionPeriodPatchRequest(string? Name = null, string? Reason = null, DateOnly? ValidFrom = null, DateOnly? ValidTo = null, string? Scope = null,
    IReadOnlyList<Guid>? ProductPublicIds = null, IReadOnlyList<int>? CategoryIds = null, string? RowVersion = null);
