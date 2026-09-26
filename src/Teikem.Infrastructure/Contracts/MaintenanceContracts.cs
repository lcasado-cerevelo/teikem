using System.Text.Json;
using System.Text.Json.Serialization;

namespace Teikem.Infrastructure.Contracts;

// Lote 4 — Mantenimiento preventivo, órdenes de trabajo y bitácora de combustible. Records sin lógica; firma posicional fija.

// ---------------------------------------------------------------- programas de mantenimiento

public sealed record MaintenanceScheduleDto(
    int Id,
    string Name,
    Guid? VehiclePublicId,
    string? VehicleCode,
    string? VehicleTypeCode,
    string? VehicleType,
    string TriggerCode,
    string Trigger,
    decimal? IntervalKm,
    int? IntervalDays,
    decimal? LastServiceKm,
    DateOnly? LastServiceDate,
    bool IsActive,
    DateTime CreatedAtUtc);

/// <summary>Alta/edición de un programa: exactamente uno de VehiclePublicId / VehicleType.</summary>
public sealed record MaintenanceScheduleRequest(
    string? Name = null,
    Guid? VehiclePublicId = null,
    string? VehicleType = null,
    string? Trigger = null,
    decimal? IntervalKm = null,
    int? IntervalDays = null,
    decimal? LastServiceKm = null,
    DateOnly? LastServiceDate = null,
    bool? ClearIntervalKm = null,
    bool? ClearIntervalDays = null);

public sealed record MaintenanceDueQuery(Guid? VehiclePublicId = null, string[]? Status = null);

/// <summary>Estado del preventivo por (programa, vehículo): State ∈ MaintenanceDueStates.</summary>
public sealed record MaintenanceDueDto(
    int ScheduleId,
    string ScheduleName,
    Guid VehiclePublicId,
    string VehicleCode,
    string TriggerCode,
    decimal? IntervalKm,
    int? IntervalDays,
    decimal? LastServiceKm,
    DateOnly? LastServiceDate,
    string? LastWorkOrderNumber,
    decimal? CurrentOdometerKm,
    decimal? NextDueKm,
    DateOnly? NextDueDate,
    decimal? KmRemaining,
    int? DaysRemaining,
    string State);

// ---------------------------------------------------------------- órdenes de trabajo

public sealed record WorkOrderListQuery(
    Guid? VehiclePublicId = null,
    string[]? Status = null,
    string[]? MaintenanceType = null,
    int? ScheduleId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    bool IncludeInactive = false,
    int Skip = 0,
    int Take = 100);

public sealed record WorkOrderListItemDto(
    int Id,
    Guid PublicId,
    string Number,
    Guid VehiclePublicId,
    string VehicleCode,
    string MaintenanceTypeCode,
    string MaintenanceType,
    int? ScheduleId,
    string? ScheduleName,
    string StatusCode,
    string Status,
    string? StatusColor,
    DateOnly? ScheduledDate,
    DateOnly? CompletedDate,
    decimal? OdometerKm,
    string? Vendor,
    decimal TotalCost,
    bool IsActive);

public sealed record MaintenanceTaskDto(int Id, string Description, decimal? PartCost, decimal? LaborCost, bool IsCompleted, bool IsActive);

public sealed record MaintenanceTaskRequest(
    string? Description = null,
    decimal? PartCost = null,
    decimal? LaborCost = null,
    bool? IsCompleted = null);

/// <summary>Ficha de la OT. CostsFromTasks = los costos son la suma de las tareas activas; CanEdit = capacidad EDIT_WORK_ORDER.</summary>
public sealed record WorkOrderDetailDto(
    int Id,
    Guid PublicId,
    string Number,
    Guid VehiclePublicId,
    string VehicleCode,
    string MaintenanceTypeCode,
    string MaintenanceType,
    int? ScheduleId,
    string? ScheduleName,
    string? ScheduleTriggerCode,
    string StatusCode,
    string Status,
    bool IsTerminal,
    DateOnly? ScheduledDate,
    DateOnly? CompletedDate,
    decimal? OdometerKm,
    string? Vendor,
    decimal? LaborCost,
    decimal? PartsCost,
    decimal TotalCost,
    string? CurrencyCode,
    string? Notes,
    bool CostsFromTasks,
    bool CanEdit,
    IReadOnlyList<MaintenanceTaskDto> Tasks,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    string RowVersion);

/// <summary>POST /maintenance-work-orders. El número OT-##### lo asigna el sistema.</summary>
public sealed record WorkOrderCreateRequest(
    Guid VehiclePublicId,
    string? MaintenanceType = null,
    int? ScheduleId = null,
    DateOnly? ScheduledDate = null,
    decimal? OdometerKm = null,
    string? Vendor = null,
    decimal? LaborCost = null,
    decimal? PartsCost = null,
    string? Currency = null,
    string? Notes = null);

/// <summary>PATCH de la OT: null = sin cambio. 'number'/'vehiclePublicId' llegan en Extra y responden 400.</summary>
public sealed record WorkOrderPatchRequest(
    DateOnly? ScheduledDate = null,
    decimal? OdometerKm = null,
    string? Vendor = null,
    decimal? LaborCost = null,
    decimal? PartsCost = null,
    string? Currency = null,
    string? Notes = null,
    string? RowVersion = null)
{
    /// <summary>Llaves del JSON que no corresponden a ninguna propiedad (el servicio rechaza las prohibidas con 400).</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; set; }
}

/// <summary>Cambio de estatus de la OT; para CLOSED admite fecha de cierre y lectura de odómetro.</summary>
public sealed record WorkOrderStatusRequest(
    string ToCode,
    string? Comment = null,
    DateOnly? CompletedDate = null,
    decimal? OdometerKm = null,
    string? RowVersion = null);

// ---------------------------------------------------------------- bitácora de combustible

/// <summary>GET /fuel-logs: FromUtc inclusivo, ToUtc exclusivo; Take 1..500.</summary>
public sealed record FuelLogQuery(
    Guid? VehiclePublicId = null,
    Guid? DriverPublicId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    bool IncludeInactive = false,
    int Skip = 0,
    int Take = 100);

public sealed record FuelLogDto(
    int Id,
    Guid VehiclePublicId,
    string VehicleCode,
    Guid? DriverPublicId,
    string? DriverName,
    DateTime FillDateUtc,
    decimal? OdometerKm,
    decimal Liters,
    decimal TotalCost,
    string? CurrencyCode,
    string? Station,
    decimal? DistanceKm,
    decimal? KmPerLiter,
    decimal? CostPerKm,
    bool IsActive);

public sealed record FuelVehicleSummaryDto(
    Guid VehiclePublicId,
    string VehicleCode,
    int Fills,
    decimal TotalLiters,
    decimal TotalCost,
    decimal? DistanceKm,
    decimal? KmPerLiter,
    decimal? CostPerKm);

public sealed record FuelLogPageDto(
    int Total,
    int Skip,
    int Take,
    IReadOnlyList<FuelLogDto> Items,
    IReadOnlyList<FuelVehicleSummaryDto> Summary);

public sealed record FuelLogCreateRequest(
    Guid VehiclePublicId,
    DateTime FillDateUtc,
    decimal Liters,
    decimal TotalCost,
    decimal? OdometerKm = null,
    Guid? DriverPublicId = null,
    string? Currency = null,
    string? Station = null);

/// <summary>PATCH de una carga: null = sin cambio; Clear* borra. El vehículo no se cambia (llega en Extra → 400).</summary>
public sealed record FuelLogPatchRequest(
    DateTime? FillDateUtc = null,
    decimal? Liters = null,
    decimal? TotalCost = null,
    decimal? OdometerKm = null,
    bool? ClearOdometer = null,
    Guid? DriverPublicId = null,
    bool? ClearDriver = null,
    string? Currency = null,
    string? Station = null)
{
    /// <summary>Llaves del JSON que no corresponden a ninguna propiedad (el servicio rechaza las prohibidas con 400).</summary>
    [JsonExtensionData]
    public IDictionary<string, JsonElement>? Extra { get; set; }
}
