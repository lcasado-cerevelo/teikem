namespace Teikem.Infrastructure.Contracts;

public sealed record CustomFieldOptionDto(int Id, string Value, string Label, IDictionary<string, string> Labels, int SortOrder, bool IsActive);
public sealed record CustomFieldOptionUpsert(string Value, IDictionary<string, string> Labels, int? SortOrder);
public sealed record CustomFieldDefinitionDto(
    int Id, string EntityType, string FieldKey, string Label, IDictionary<string, string> Labels, string? Description, string DataType,
    bool IsRequired, bool IsUnique, string? DefaultValue, string? ValidationJson, string? RefEntity, bool ShowInList, int SortOrder, bool IsActive,
    IReadOnlyList<CustomFieldOptionDto> Options);
public sealed record CustomFieldDefinitionUpsert(
    string FieldKey, IDictionary<string, string> Labels, IDictionary<string, string>? Descriptions, string DataType,
    bool IsRequired, bool IsUnique, string? DefaultValue, string? ValidationJson, string? RefEntity, bool ShowInList, int? SortOrder,
    IList<CustomFieldOptionUpsert>? Options);
public sealed record CustomFieldValueDto(string FieldKey, string DataType, object? Value, string? DisplayValue);
public sealed record CustomFieldValuesRequest(IDictionary<string, object?> Values);
