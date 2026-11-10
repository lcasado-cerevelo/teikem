namespace Teikem.Infrastructure.Contracts;

// Lote 232 — kit ARMADO (producto con inventario propio). Firma posicional FIJA. Productos por PublicId; posiciones y lotes por id entero (resueltos dentro de
// su almacén / producto filtrados). Costos (UnitCost / TotalCost) solo con inventory.value; sin él viajan null.

/// <summary>Pieza de la composición: producto y cantidad por kit (&gt; 0).</summary>
public sealed record AssembledKitPieceInput(Guid? ProductPublicId, decimal? Quantity);

/// <summary>PUT /products/{id}/assembled-kit: reemplaza la composición (solo sin armados, movimientos ni existencia del kit).</summary>
public sealed record AssembledKitSetRequest(IReadOnlyList<AssembledKitPieceInput>? Pieces);

/// <summary>Escoger de dónde sale una pieza al armar: un lote (de la pieza) y/o una posición. Sin escoger, sale por la rotación de siempre (FEFO/FIFO).</summary>
public sealed record AssemblyPieceChoice(Guid? ProductPublicId, int? LotId = null, int? BinId = null);

/// <summary>POST /products/{id}/assembled-kit/assemblies: armar Quantity kits (entero) en la posición ToBinId del almacén (o el único activo).</summary>
public sealed record KitAssembleRequest(decimal? Quantity, int? ToBinId, Guid? WarehousePublicId = null, string? Notes = null,
    IReadOnlyList<AssemblyPieceChoice>? Pieces = null);

/// <summary>
/// POST /products/{id}/assembled-kit/assemblies/{assemblyId}/disassemble: desarmar Quantity kits de ese armado. Los kits salen del almacén indicado (por omisión, el del
/// armado), de FromBinId si se indica (si no, de donde haya disponibles); las piezas vuelven a ToBinId o, sin ella, a la posición de donde salieron.
/// </summary>
public sealed record KitDisassembleRequest(decimal? Quantity, Guid? WarehousePublicId = null, int? FromBinId = null, int? ToBinId = null, string? Notes = null);

public sealed record AssembledKitPieceDto(Guid ProductPublicId, string Sku, string Name, decimal Quantity, int SortOrder, bool IsActive, string TrackingTypeCode);

/// <summary>«Se pueden armar»: CanAssemble = en UN armado (un solo lote por pieza); CanAssembleTotal = juntando todos los lotes (en varios armados).</summary>
public sealed record AssembledKitAvailabilityDto(Guid WarehousePublicId, string WarehouseCode, int CanAssemble, int CanAssembleTotal);

/// <summary>
/// GET /products/{id}/assembled-kit. IsAssembledKit=false (sin piezas ni disponibilidad) si el producto no es kit armado. CompositionLocked = ya tiene armados, movimientos
/// o existencia (la composición no se cambia: se crea un kit nuevo). QtyOnHand / QtyAvailable = kits en existencia y disponibles (no reservados) de todos los almacenes.
/// </summary>
public sealed record AssembledKitDto(Guid ProductPublicId, string Sku, string Name, bool IsAssembledKit, string TrackingTypeCode, bool CompositionLocked,
    IReadOnlyList<AssembledKitPieceDto> Pieces, IReadOnlyList<AssembledKitAvailabilityDto> Availability, decimal QtyOnHand, decimal QtyAvailable);

public sealed record KitAssemblyLineDto(Guid ProductPublicId, string Sku, string Name, int? LotId, string? LotNumber, DateOnly? ExpiryDate, int FromBinId,
    string FromBinCode, decimal QtyPerKit, decimal Quantity, decimal ReturnedQty, decimal? UnitCost);

public sealed record KitDisassemblyLineDto(Guid ProductPublicId, string Sku, string Name, int? LotId, string? LotNumber, int ToBinId, string ToBinCode,
    decimal Quantity, decimal? UnitCost);

public sealed record KitDisassemblyDto(Guid PublicId, string Number, decimal Quantity, Guid WarehousePublicId, string WarehouseCode, string? Notes,
    DateTime CreatedAtUtc, int? CreatedBy, string? CreatedByName, IReadOnlyList<KitDisassemblyLineDto> Lines);

/// <summary>Un armado con sus piezas (lote y cantidad de cada una) y sus desarmados. Remaining = kits de este armado que aún se pueden desarmar (sin contar reservas).</summary>
public sealed record KitAssemblyDto(Guid PublicId, string Number, Guid KitPublicId, string KitSku, decimal Quantity, decimal DisassembledQty, decimal Remaining,
    Guid WarehousePublicId, string WarehouseCode, int ToBinId, string ToBinCode, int? KitLotId, string? KitLotNumber, DateOnly? KitLotExpiry,
    decimal? UnitCost, decimal? TotalCost, string? Notes, DateTime CreatedAtUtc, int? CreatedBy, string? CreatedByName,
    IReadOnlyList<KitAssemblyLineDto> Lines, IReadOnlyList<KitDisassemblyDto> Disassemblies);

public sealed record KitAssemblyPageDto(int Total, int Skip, int Take, IReadOnlyList<KitAssemblyDto> Items);

/// <summary>Resultado de armar: el armado y los movimientos/saldos tocados (salen las piezas, entran los kits).</summary>
public sealed record KitAssemblyResultDto(KitAssemblyDto Assembly, MovementResultDto Movements);

/// <summary>Resultado de desarmar: el armado (con su nuevo pendiente), el desarmado y los movimientos/saldos tocados.</summary>
public sealed record KitDisassemblyResultDto(KitAssemblyDto Assembly, KitDisassemblyDto Disassembly, MovementResultDto Movements);
