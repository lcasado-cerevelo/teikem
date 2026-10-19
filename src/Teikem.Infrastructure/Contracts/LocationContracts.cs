namespace Teikem.Infrastructure.Contracts;

/// <summary>
/// Consignatario o localización del tenant (Lote 2, P2). Una fila con ClientPublicId = null es una localización
/// compartida del operador; con cliente, es propia de ese cliente (consignatario, almacén, dirección corporativa/postal).
/// </summary>
public sealed record LocationDto(
    int Id,
    Guid PublicId,
    Guid? ClientPublicId,
    string? ClientName,
    bool IsShared,
    string? Code,
    string Name,
    string LocationType,
    string LocationTypeLabel,
    string Line1,
    string? Line2,
    string City,
    string? State,
    string? PostalCode,
    string Country,
    string CountryLabel,
    int DefaultServiceMinutes,
    TimeOnly? DefaultWindowStart,
    TimeOnly? DefaultWindowEnd,
    string? AccessNotes,
    string? DeliveryNotes,
    bool AllowDupInvoice,
    bool IsActive,
    DateTime CreatedAtUtc,
    string RowVersion,
    string? ContactName = null);

/// <summary>
/// Filtro de la lista. Con ClientPublicId devuelve las propias del cliente y, si IncludeShared, también las compartidas
/// (ClientId NULL); sin ClientPublicId devuelve todas las del tenant (pantalla de Catálogo). IncludeInactive=false
/// excluye IsActive=0 (R5: una localización inactiva no aparece en los combos pero conserva su historial).
/// </summary>
public sealed record LocationQuery(
    Guid? ClientPublicId,
    bool IncludeShared = true,
    bool IncludeInactive = false,
    string? LocationType = null,
    string? Search = null,
    // Lote 130: filtros por campos personalizados con «Mostrar en la búsqueda»
    IReadOnlyList<Teikem.Infrastructure.Services.CustomFieldFilter>? CustomFields = null);

/// <summary>Alta de localización. ClientPublicId null = compartida del tenant. Country por defecto 'PR'. ContactName (Lote 45): persona de contacto, opcional (150).</summary>
public sealed record LocationUpsertRequest(
    Guid? ClientPublicId,
    string? Code,
    string Name,
    string LocationType,
    string Line1,
    string? Line2,
    string City,
    string? State,
    string? PostalCode,
    string? Country,
    int DefaultServiceMinutes = 0,
    TimeOnly? DefaultWindowStart = null,
    TimeOnly? DefaultWindowEnd = null,
    string? AccessNotes = null,
    string? DeliveryNotes = null,
    bool AllowDupInvoice = false,
    string? ContactName = null);

/// <summary>
/// Edición parcial: null = sin cambio. En los textos opcionales (Code, Line2, State, PostalCode, AccessNotes,
/// DeliveryNotes, ContactName) la cadena vacía borra el valor. MakeShared=true quita el dueño (excluyente con ClientPublicId);
/// ClearWindow=true borra la ventana horaria. RowVersion (base64 de la ficha) activa el control de concurrencia (409).
/// </summary>
public sealed record LocationPatchRequest(
    Guid? ClientPublicId = null,
    bool? MakeShared = null,
    string? Code = null,
    string? Name = null,
    string? LocationType = null,
    string? Line1 = null,
    string? Line2 = null,
    string? City = null,
    string? State = null,
    string? PostalCode = null,
    string? Country = null,
    int? DefaultServiceMinutes = null,
    TimeOnly? DefaultWindowStart = null,
    TimeOnly? DefaultWindowEnd = null,
    bool? ClearWindow = null,
    string? AccessNotes = null,
    string? DeliveryNotes = null,
    bool? AllowDupInvoice = null,
    string? RowVersion = null,
    string? ContactName = null);
