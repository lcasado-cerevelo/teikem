using Teikem.Infrastructure.Analytics;

namespace Teikem.Infrastructure.Contracts;

public sealed record DataFieldDto(string Key, string Label, string Type, bool IsMoney);
public sealed record DataRelationDto(string Key, string TargetSource, string Label);
public sealed record DataSourceDto(string Key, string Label, string? EntityType, string? DateField, string DefaultBusinessModule, IReadOnlyList<DataFieldDto> Fields, IReadOnlyList<DataRelationDto> Relations, IReadOnlyList<DataFieldDto> CustomFields);

public sealed record ShareDto(int? UserId, int? RoleId, bool CanEdit);

public sealed record ReportDto(
    int Id, Guid PublicId, string BaseEntityType, string Name, string? Description, IDictionary<string, string> Descriptions,
    string Visibility, int? OwnerUserId, string? OwnerName, bool IsSystem, bool CanEdit,
    IReadOnlyList<string> Secondary, IReadOnlyList<string> Columns, string? FilterJson, string? SortJson, string? GroupJson,
    string? ChartType, string? ScheduleCron, string? DeliveryEmails, IReadOnlyList<ShareDto> Shares, bool IsActive);

public sealed record ReportUpsertRequest(
    string Name, IDictionary<string, string>? Descriptions, string? Visibility, IList<string>? Secondary, IList<string> Columns,
    string? FilterJson, string? SortJson, string? GroupJson, string? ChartType, string? ScheduleCron, string? DeliveryEmails, IList<ShareDto>? Shares);

public sealed record ReportRunRequest(string? DateRangeMode, DateOnly? DateFrom, DateOnly? DateTo, int? Skip, int? Take);
public sealed record ReportRunResultDto(IReadOnlyList<ReportColumn> Columns, IReadOnlyList<IDictionary<string, object?>> Rows, IDictionary<string, object?>? Totals, int Total);

public sealed record AnalyticsDefinitionDto(
    int Id, Guid PublicId, string Name, string? Description, IReadOnlyDictionary<string, string> Descriptions, string DataSource, string? Field, string AggregateFn, string? FilterJson,
    string BusinessModule, bool IsMoney, bool IsSystem, int? OwnerUserId, string? OwnerName, string Visibility,
    string? DateRangeMode, DateOnly? DateFrom, DateOnly? DateTo, bool ShowInPulse,
    bool CanEdit, bool CanChangeDate, bool DateRangeApplies,
    // preferencia efectiva del usuario actual
    string? EffectiveDateRangeMode, DateOnly? EffectiveDateFrom, DateOnly? EffectiveDateTo, bool EffectiveShowInPulse,
    string? GroupByField, string? ChartType, IReadOnlyList<ShareDto> Shares, int SortOrder);

public sealed record IndicatorUpsertRequest(
    string Name, IDictionary<string, string>? Descriptions, string DataSource, string? Field, string AggregateFn, string? FilterJson,
    string? BusinessModule, bool? IsMoney, string? Visibility, string? DateRangeMode, DateOnly? DateFrom, DateOnly? DateTo, bool? ShowInPulse, IList<ShareDto>? Shares, int? SortOrder);

public sealed record ChartUpsertRequest(
    string Name, IDictionary<string, string>? Descriptions, string DataSource, string GroupByField, string? Field, string AggregateFn, string? ChartType, string? FilterJson,
    string? BusinessModule, bool? IsMoney, string? Visibility, string? DateRangeMode, DateOnly? DateFrom, DateOnly? DateTo, bool? ShowInPulse, IList<ShareDto>? Shares, int? SortOrder);

public sealed record DateRangeRequest(string DateRangeMode, DateOnly? DateFrom, DateOnly? DateTo);
public sealed record PulseRequest(bool ShowInPulse);
/// <summary>
/// Valor de un indicador. Lote F8a: BusinessModule, orden y visibilidad efectivos en el Pulso de este usuario y su origen
/// (Source: "user" = preferencia propia, "company" = la definición). En el Pulso, un elemento oculto viaja sin calcular (Value null).
/// </summary>
public sealed record IndicatorValueDto(int Id, string Name, decimal? Value, bool IsMoney, string? DateRangeMode,
    DateTime? FromUtc, DateTime? ToUtc, string BusinessModule, int SortOrder, bool IsVisible, string Source);
/// <summary>Datos de un gráfico; Lote F8a: igual que <see cref="IndicatorValueDto"/> (oculto en el Pulso → Points vacío).</summary>
public sealed record ChartDataDto(int Id, string Name, string ChartType, bool IsMoney, IReadOnlyList<ChartPoint> Points, string? DateRangeMode,
    DateTime? FromUtc, DateTime? ToUtc, string BusinessModule, int SortOrder, bool IsVisible, string Source);

// ---- Lote F8a (P1): Pulso por paneles, orden en dos niveles (compañía / usuario) ----

/// <summary>Panel del Pulso que el usuario puede ver. Source: "user" | "company" | "default" (registro PulsePanels).</summary>
public sealed record PulsePanelDto(string Key, bool IsVisible, int SortOrder, string Source);
/// <summary>
/// Pulso del usuario: solo los paneles que puede ver (permiso + permisos de datos + módulo), en su orden efectivo, incluidos los
/// ocultos (IsVisible=false) para el modo Organizar; dentro de INDICATORS/CHARTS solo los elementos que puede leer.
/// </summary>
public sealed record PulseDto(IReadOnlyList<IndicatorValueDto> Indicators, IReadOnlyList<ChartDataDto> Charts,
    IReadOnlyList<PulsePanelDto> Panels, bool HasPersonalLayout, bool CanOrganizeCompany);
/// <summary>Orden y visibilidad de un indicador o gráfico. Kind: "indicator" | "chart".</summary>
public sealed record PulseLayoutItem(string Kind, int Id, int SortOrder, bool IsVisible);
/// <summary>Orden y visibilidad de un panel del registro (INDICATORS, CHARTS, WAREHOUSE, ACTIVITY).</summary>
public sealed record PulseLayoutPanel(string Key, int SortOrder, bool IsVisible);
/// <summary>Cuerpo de PUT /analytics/pulse/layout: solo se escribe lo que viene (lo ausente no se toca).</summary>
public sealed record PulseLayoutRequest(IList<PulseLayoutItem>? Items, IList<PulseLayoutPanel>? Panels);
