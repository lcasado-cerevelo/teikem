namespace Teikem.Infrastructure.Contracts;

// Lote 245 (Lote B del plan de rutas, cadenas y precios) — cadenas de clientes (/api/v1/client-chains). Un cliente pertenece a UNA sola cadena; la cadena se
// expone por su PublicId (nunca el id interno). Las acciones de una asignación o del alta masiva son las de ClientChainCodes (ASSIGN, MOVE, ALREADY,
// SKIP_OTHER_CHAIN, EXCLUDED). RowVersion en base64.

// ---------------------------------------------------------------- la cadena

/// <summary>Renglón de la lista de cadenas. ClientCount = clientes ACTIVOS de la cadena.</summary>
public sealed record ClientChainListItemDto(Guid PublicId, string Name, string? Notes, bool IsActive, int ClientCount);

/// <summary>Ficha de la cadena: ClientCount = clientes activos; InactiveClientCount = clientes dados de baja que conservan la cadena.</summary>
public sealed record ClientChainDetailDto(Guid PublicId, string Name, string? Notes, bool IsActive, int ClientCount, int InactiveClientCount,
    DateTime CreatedAtUtc, DateTime? UpdatedAtUtc, string RowVersion);

/// <summary>Crear o editar una cadena. Al editar, null = no cambia; ClearNotes = borrar las notas. RowVersion opcional (409 si cambió).</summary>
public sealed record ClientChainRequest(string? Name, string? Notes, bool ClearNotes = false, string? RowVersion = null);

// ---------------------------------------------------------------- clientes de la cadena

/// <summary>Un cliente de la cadena (Status = código interno del estatus; StatusLabel = su etiqueta en el idioma del usuario).</summary>
public sealed record ClientChainClientDto(Guid ClientPublicId, string Code, string Name, string Status, string StatusLabel, bool IsActive, string? SalesRepName);

/// <summary>Asignar clientes a la cadena: los que están en otra se saltan (SkippedOtherChain) salvo con MoveFromOtherChains.</summary>
public sealed record ClientChainAssignRequest(IReadOnlyList<Guid>? ClientPublicIds, bool MoveFromOtherChains = false);

/// <summary>Quitar clientes de la cadena: los que no están en ESTA cadena se cuentan en NotInChain (idempotente).</summary>
public sealed record ClientChainRemoveRequest(IReadOnlyList<Guid>? ClientPublicIds);

/// <summary>Un cliente que no se asignó porque ya está en otra cadena (y no se pidió mover).</summary>
public sealed record ClientChainSkippedDto(Guid ClientPublicId, string Code, string Name, Guid ChainPublicId, string ChainName);

/// <summary>Resultado de asignar o quitar clientes.</summary>
public sealed record ClientChainAssignResultDto(int Assigned, int Moved, int AlreadyInChain, int Removed, int NotInChain,
    IReadOnlyList<ClientChainSkippedDto> SkippedOtherChain);

/// <summary>Cambiar la cadena de un cliente desde su ficha (PUT by-client/{id}): null = quitarlo de su cadena. Puede mover entre cadenas en un solo paso.</summary>
public sealed record ClientChainSetRequest(Guid? ChainPublicId);

/// <summary>La cadena de un cliente (ChainPublicId null = sin cadena; ChainIsActive = la cadena sigue activa).</summary>
public sealed record ClientChainMembershipDto(Guid ClientPublicId, Guid? ChainPublicId, string? ChainName, bool ChainIsActive);

// ---------------------------------------------------------------- alta masiva por el inicio del nombre

/// <summary>
/// Alta masiva por el INICIO del nombre: Prefix (mínimo 3 letras, sin distinguir mayúsculas, acentos, puntos ni comas); WholeWord = palabra completa
/// («Walgreens» no toma «Walgreensboro»); MoveFromOtherChains = mover también los que están en otra cadena; ExcludeClientPublicIds = los que se quitan a mano
/// (no entran en la huella); Fingerprint = la huella de la simulación (obligatoria al aplicar).
/// </summary>
public sealed record ClientChainBulkRequest(string? Prefix, bool WholeWord = true, bool MoveFromOtherChains = false,
    IReadOnlyList<Guid>? ExcludeClientPublicIds = null, string? Fingerprint = null);

/// <summary>
/// La simulación del alta masiva (no escribe nada): conteos por acción, TooMany = más coincidencias que MaxBulk (no se puede aplicar), Fingerprint = la huella que
/// pide aplicar, Rows = los clientes encontrados (a lo sumo MaxBulk, por nombre).
/// </summary>
public sealed record ClientChainBulkPreviewDto(string Prefix, bool WholeWord, bool MoveFromOtherChains, int Matched, int ToAssign, int ToMove, int AlreadyInChain,
    int SkippedOtherChain, int Excluded, bool TooMany, int MaxBulk, string Fingerprint, IReadOnlyList<ClientChainBulkRowDto> Rows);

/// <summary>Un cliente de la simulación con su acción y la cadena en la que está hoy (null = sin cadena).</summary>
public sealed record ClientChainBulkRowDto(Guid ClientPublicId, string Code, string Name, string Action, Guid? CurrentChainPublicId, string? CurrentChainName);

/// <summary>Resultado de aplicar el alta masiva.</summary>
public sealed record ClientChainBulkResultDto(int Assigned, int Moved, int AlreadyInChain, int SkippedOtherChain, int Excluded);
