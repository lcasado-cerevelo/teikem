using Teikem.Domain.Catalogs;
using Teikem.Domain.Common;

namespace Teikem.Domain.Fleet;

/// <summary>
/// Programa de mantenimiento preventivo: por vehículo (VehicleId) o por tipo de vehículo (VehicleTypeLookupId), nunca ambos
/// (CK_MaintSchedule_Target). Disparador MILEAGE/TIME/BOTH con su intervalo (CK_MaintSchedule_Interval). LastServiceKm /
/// LastServiceDate son la línea base de un programa por vehículo; por tipo se toma la última OT cerrada de cada vehículo.
/// </summary>
[AuditEntity(Constants.EntityTypes.MaintenanceSchedule)]
public class MaintenanceSchedule : ITenantScoped, ISoftDeletable
{
    public int MaintenanceScheduleId { get; set; }
    public int TenantId { get; set; }
    public int? VehicleId { get; set; }
    public int? VehicleTypeLookupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int TriggerLookupId { get; set; }
    public decimal? IntervalKm { get; set; }
    public int? IntervalDays { get; set; }
    public decimal? LastServiceKm { get; set; }
    public DateOnly? LastServiceDate { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Vehicle? Vehicle { get; set; }
    public LookupCode? VehicleType { get; set; }
    public LookupCode? Trigger { get; set; }
}

/// <summary>
/// Orden de trabajo de mantenimiento (EntityType WORK_ORDER, ya sembrado). Número automático OT-##### por compañía
/// (NumberSequence WORKORDER, UQ_WorkOrder_Number). Estatus WorkOrderStatus: OPEN → IN_PROGRESS → CLOSED; CANCELLED
/// terminal. Con tareas activas, LaborCost/PartsCost son la suma de las tareas; TotalCost es una columna computada de SQL
/// (solo lectura).
/// </summary>
[AuditEntity(Constants.EntityTypes.WorkOrder)]
public class MaintenanceWorkOrder : ITenantScoped, ISoftDeletable, IHasStatus, IAuditStamped
{
    public int WorkOrderId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public int VehicleId { get; set; }
    public int? MaintenanceScheduleId { get; set; }
    public string Number { get; set; } = string.Empty;
    public int MaintenanceTypeLookupId { get; set; }
    public int StatusCodeId { get; set; }
    public decimal? OdometerKm { get; set; }
    public DateOnly? ScheduledDate { get; set; }
    public DateOnly? CompletedDate { get; set; }
    public string? Vendor { get; set; }
    public decimal? LaborCost { get; set; }
    public decimal? PartsCost { get; set; }
    /// <summary>ISNULL(LaborCost,0) + ISNULL(PartsCost,0), PERSISTED en SQL. EF nunca la escribe.</summary>
    [NotAudited] public decimal TotalCost { get; set; }
    public int? CurrencyLookupId { get; set; }
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }

    public Vehicle? Vehicle { get; set; }
    public MaintenanceSchedule? Schedule { get; set; }
    public LookupCode? MaintenanceType { get; set; }
    public StatusCode? Status { get; set; }
    public LookupCode? Currency { get; set; }
    public ICollection<MaintenanceTask> Tasks { get; set; } = new List<MaintenanceTask>();
}

/// <summary>Tarea de una orden de trabajo. Sin TenantId: se alcanza a través de la OT; auditada bajo WORK_ORDER.</summary>
[AuditEntity(Constants.EntityTypes.WorkOrder)]
public class MaintenanceTask : ISoftDeletable
{
    public int MaintenanceTaskId { get; set; }
    public int WorkOrderId { get; set; }
    public string Description { get; set; } = string.Empty;
    public decimal? PartCost { get; set; }
    public decimal? LaborCost { get; set; }
    public bool IsCompleted { get; set; }
    public bool IsActive { get; set; } = true;

    public MaintenanceWorkOrder? WorkOrder { get; set; }
}
