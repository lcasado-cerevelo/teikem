namespace Teikem.Infrastructure.Contracts;

// Lote 6 — Cruce de muelle (demo, módulo CROSSDOCK). Firma posicional FIJA. Citas, planes y asignaciones por id int,
// resueltos siempre dentro de su almacén o plan filtrado; la orden por PublicId.

public sealed record DockAppointmentRequest(Guid? WarehousePublicId, int? DockId, string? Direction, DateTime? ScheduledStartUtc,
    DateTime? ScheduledEndUtc = null, int? AsnId = null, Guid? TripPublicId = null);

public sealed record DockAppointmentPatchRequest(DateTime? ScheduledStartUtc = null, DateTime? ScheduledEndUtc = null, int? DockId = null);

public sealed record DockAppointmentStatusRequest(string? Status, string? Comment = null);

public sealed record DockAppointmentQuery(Guid? WarehousePublicId = null, int? DockId = null, DateTime? FromUtc = null, DateTime? ToUtc = null,
    string[]? Status = null);

public sealed record DockAppointmentDto(int Id, Guid WarehousePublicId, int DockId, string DockCode, string DockTypeCode, string DockStatusCode,
    string DirectionCode, string Direction, DateTime ScheduledStartUtc, DateTime? ScheduledEndUtc, int? AsnId, string? AsnReference,
    Guid? TripPublicId, string? TripCode, string StatusCode, string Status, string? StatusColor);

public sealed record CrossDockPlanRequest(Guid? WarehousePublicId = null, int? StagingZoneId = null);

public sealed record CrossDockAllocationRequest(int? ReceiptLineId, Guid? OrderPublicId, decimal? Quantity, int? CargoLineId = null);

public sealed record CrossDockAllocationDto(int Id, int ReceiptLineId, string ReceiptNumber, string ReceiptStatusCode, Guid ProductPublicId,
    string Sku, string? LotNumber, Guid OrderPublicId, string PackBatchNumber, string ClientName, decimal Quantity, decimal? ConfirmedQty,
    decimal ShortQty, string StatusCode, string Status, int? TaskId, long? InventoryTransactionId);

public sealed record CrossDockPlanDto(int Id, string Number, Guid WarehousePublicId, string WarehouseCode, int? StagingZoneId,
    string? StagingZoneCode, string StatusCode, string Status, int AllocationCount, decimal AllocatedQty, decimal MovedQty, decimal ShortQty,
    DateTime CreatedAtUtc, DateTime? CompletedAtUtc, IReadOnlyList<CrossDockAllocationDto> Allocations);

public sealed record CrossDockCandidateDto(int ReceiptLineId, string ReceiptNumber, string ReceiptStatusCode, Guid ProductPublicId, string Sku,
    string? LotNumber, int? StagingBinId, string? StagingBinCode, decimal BaseQty, decimal AllocatedQty, decimal Allocatable);

public sealed record CrossDockMoveRequest(string? Comment = null);
