using Teikem.Domain.Common;

namespace Teikem.Domain.CustomFields;

[AuditEntity(Constants.EntityTypes.CustomFieldDefinition)]
public class CustomFieldDefinition : ITenantScoped, ISoftDeletable, IAuditStamped
{
    public int CustomFieldDefinitionId { get; set; }
    public int TenantId { get; set; }
    public int EntityTypeLookupId { get; set; }
    public string FieldKey { get; set; } = string.Empty;
    public string LabelJson { get; set; } = "{}";
    public string? DescriptionJson { get; set; }
    public int DataTypeLookupId { get; set; }
    public bool IsRequired { get; set; }
    public bool IsUnique { get; set; }
    public string? DefaultValue { get; set; }
    public string? ValidationJson { get; set; }
    /// <summary>Para LOOKUP_REF (y SELECT/MULTISELECT atados a una lista del catálogo): dominio LookupCode referenciado.</summary>
    public string? RefEntity { get; set; }
    public bool ShowInList { get; set; }
    public int SortOrder { get; set; } = 100;
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    [NotAudited] public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    [NotAudited] public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }

    public Catalogs.LookupCode? EntityType { get; set; }
    public Catalogs.LookupCode? DataType { get; set; }
    public ICollection<CustomFieldOption> Options { get; set; } = new List<CustomFieldOption>();
}

[AuditEntity(Constants.EntityTypes.CustomFieldDefinition)]
public class CustomFieldOption : ISoftDeletable
{
    public int CustomFieldOptionId { get; set; }
    public int CustomFieldDefinitionId { get; set; }
    public string OptionValue { get; set; } = string.Empty;
    public string LabelJson { get; set; } = "{}";
    public int SortOrder { get; set; } = 100;
    public bool IsActive { get; set; } = true;
    public CustomFieldDefinition? Definition { get; set; }
}

/// <summary>EAV tipado: el valor va en la columna de su tipo para poder filtrar/ordenar.</summary>
public class CustomFieldValue : ITenantScoped
{
    public long CustomFieldValueId { get; set; }
    public int TenantId { get; set; }
    public int CustomFieldDefinitionId { get; set; }
    public int EntityId { get; set; }
    public string? ValueText { get; set; }
    public decimal? ValueNumber { get; set; }
    public DateTime? ValueDate { get; set; }
    public bool? ValueBool { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? UpdatedBy { get; set; }
    public CustomFieldDefinition? Definition { get; set; }
}
