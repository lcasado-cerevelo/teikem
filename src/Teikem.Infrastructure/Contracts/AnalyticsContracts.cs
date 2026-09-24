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
    int Id, Guid PublicId, string Name, string? Description, string DataSource, string? Field, string AggregateFn, string? FilterJson,
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
public sealed record IndicatorValueDto(int Id, string Name, decimal? Value, bool IsMoney, string? DateRangeMode, DateTime? FromUtc, DateTime? ToUtc);
public sealed record ChartDataDto(int Id, string Name, string ChartType, bool IsMoney, IReadOnlyList<ChartPoint> Points, string? DateRangeMode, DateTime? FromUtc, DateTime? ToUtc);
public sealed record PulseDto(IReadOnlyList<IndicatorValueDto> Indicators, IReadOnlyList<ChartDataDto> Charts);
