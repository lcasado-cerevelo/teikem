using Teikem.Domain.Common;

namespace Teikem.Domain.Analytics;

/// <summary>Módulo G: informe guardado sobre una fuente de datos (entidad base) + fuentes combinadas + campos personalizados.</summary>
[AuditEntity(Constants.EntityTypes.ReportDefinition)]
public class ReportDefinition : ITenantScoped, ISoftDeletable, IAuditStamped
{
    public int ReportDefinitionId { get; set; }
    [NotAudited] public Guid PublicId { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }
    public int BaseEntityTypeLookupId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DescriptionJson { get; set; }
    public int VisibilityLookupId { get; set; }
    public int? OwnerUserId { get; set; }
    public string ColumnsJson { get; set; } = "[]";
    public string? FilterJson { get; set; }
    public string? SortJson { get; set; }
    public string? GroupJson { get; set; }
    public int? ChartTypeLookupId { get; set; }
    public string? ScheduleCron { get; set; }
    public string? DeliveryEmails { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    [NotAudited] public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    [NotAudited] public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }
    // --- Extensiones Lote 1 ---
    /// <summary>Informe por default del tenant: nadie lo edita ni lo elimina.</summary>
    public bool IsSystem { get; set; }
    /// <summary>Fuentes secundarias combinadas (muchos-a-uno), JSON array de claves de fuente.</summary>
    public string? SecondaryJson { get; set; }

    public Catalogs.LookupCode? BaseEntityType { get; set; }
    public Catalogs.LookupCode? Visibility { get; set; }
    public ICollection<ReportShare> Shares { get; set; } = new List<ReportShare>();
}

[AuditEntity(Constants.EntityTypes.ReportDefinition)]
public class ReportShare
{
    public int ReportShareId { get; set; }
    public int ReportDefinitionId { get; set; }
    public int? RoleId { get; set; }
    public int? UserId { get; set; }
    public bool CanEdit { get; set; }
    public ReportDefinition? Report { get; set; }
}

/// <summary>Campos comunes de Indicador y Gráfico (módulos H e I).</summary>
public abstract class AnalyticsDefinitionBase : ITenantScoped, ISoftDeletable, IAuditStamped
{
    [NotAudited] public Guid PublicId { get; set; } = Guid.NewGuid();
    public int TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? DescriptionJson { get; set; }
    /// <summary>Fuente de datos (clave del registro de fuentes; coincide con EntityType cuando aplica).</summary>
    public string DataSourceKey { get; set; } = string.Empty;
    /// <summary>Campo sobre el que se agrega (NULL para COUNT).</summary>
    public string? FieldKey { get; set; }
    public int AggregateFnLookupId { get; set; }
    public string? FilterJson { get; set; }
    /// <summary>Categoría de negocio (OPERATIONS/WAREHOUSE/ACCOUNTING), independiente de la fuente.</summary>
    public int BusinessModuleLookupId { get; set; }
    public bool IsMoney { get; set; }
    public bool IsSystem { get; set; }
    public int? OwnerUserId { get; set; }
    public int VisibilityLookupId { get; set; }
    /// <summary>Rango por defecto (LAST7/LAST30/THIS_MONTH/CUSTOM/ALL); la preferencia por usuario lo pisa.</summary>
    public int? DateRangeModeLookupId { get; set; }
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    /// <summary>Default de "mostrar en Pulso del día"; la preferencia por usuario lo pisa.</summary>
    public bool ShowInPulse { get; set; }
    public int SortOrder { get; set; } = 100;
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    [NotAudited] public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    [NotAudited] public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }
}

/// <summary>Módulo H: un solo valor agregado sobre una fuente de datos.</summary>
[AuditEntity(Constants.EntityTypes.IndicatorDefinition)]
public class IndicatorDefinition : AnalyticsDefinitionBase
{
    public int IndicatorDefinitionId { get; set; }
    public ICollection<IndicatorShare> Shares { get; set; } = new List<IndicatorShare>();
}

[AuditEntity(Constants.EntityTypes.IndicatorDefinition)]
public class IndicatorShare
{
    public int IndicatorShareId { get; set; }
    public int IndicatorDefinitionId { get; set; }
    public int? RoleId { get; set; }
    public int? UserId { get; set; }
    public IndicatorDefinition? Indicator { get; set; }
}

/// <summary>Módulo I: una vista con un campo de agrupación y un agregado, dibujada como barra/dona/línea.</summary>
[AuditEntity(Constants.EntityTypes.ChartDefinition)]
public class ChartDefinition : AnalyticsDefinitionBase
{
    public int ChartDefinitionId { get; set; }
    public string GroupByField { get; set; } = string.Empty;
    /// <summary>Entity='ReportChartType': BAR / DONUT / LINE.</summary>
    public int ChartTypeLookupId { get; set; }
    public ICollection<ChartShare> Shares { get; set; } = new List<ChartShare>();
}

[AuditEntity(Constants.EntityTypes.ChartDefinition)]
public class ChartShare
{
    public int ChartShareId { get; set; }
    public int ChartDefinitionId { get; set; }
    public int? RoleId { get; set; }
    public int? UserId { get; set; }
    public ChartDefinition? Chart { get; set; }
}

/// <summary>
/// Preferencia de visualización por usuario (nota "Pulso del día por usuario"): qué trae a su Pulso y en qué
/// ventana de tiempo lo ve. El valor de la definición actúa como default cuando no hay preferencia.
/// </summary>
public class UserAnalyticsPreference : ITenantScoped
{
    public long UserAnalyticsPreferenceId { get; set; }
    public int TenantId { get; set; }
    public int UserId { get; set; }
    public int? IndicatorDefinitionId { get; set; }
    public int? ChartDefinitionId { get; set; }
    public bool? ShowInPulse { get; set; }
    public int? DateRangeModeLookupId { get; set; }
    public DateOnly? DateFrom { get; set; }
    public DateOnly? DateTo { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
