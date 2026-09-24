using Teikem.Domain.Common;

namespace Teikem.Domain.Catalogs;

/// <summary>Registro de dominios de catálogo (Scope 1=Lookup, 2=Status). TenantId NULL = dominio global sembrado.</summary>
[AuditEntity(Constants.EntityTypes.CatalogDomain)]
public class CatalogDomain : IOptionallyTenantScoped, ISoftDeletable
{
    public int CatalogDomainId { get; set; }
    public string DomainKey { get; set; } = string.Empty;
    public byte Scope { get; set; }
    public string LabelJson { get; set; } = "{}";
    public string? Description { get; set; }
    public bool IsSystem { get; set; } = true;
    public bool IsActive { get; set; } = true;
    /// <summary>Lote 1: listas creadas por un tenant (catálogo de listas para campos personalizados).</summary>
    public int? TenantId { get; set; }
}

/// <summary>Valor de clasificación plano, multilingüe, con override por tenant.</summary>
[AuditEntity(Constants.EntityTypes.LookupCode)]
public class LookupCode : IOptionallyTenantScoped, ISoftDeletable
{
    public int LookupCodeId { get; set; }
    public string Entity { get; set; } = string.Empty;
    public string InternalCode { get; set; } = string.Empty;
    public string LabelJson { get; set; } = "{}";
    public string? DescriptionJson { get; set; }
    public string? ExtraJson { get; set; }
    public int SortOrder { get; set; } = 100;
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }
    /// <summary>Lote 1: valor de una lista propia del tenant (NULL = valor global).</summary>
    public int? TenantId { get; set; }

    public CatalogDomain? Domain { get; set; }
    public ICollection<LookupCodeOverride> Overrides { get; set; } = new List<LookupCodeOverride>();
}

[AuditEntity(Constants.EntityTypes.LookupCode)]
public class LookupCodeOverride : ITenantScoped
{
    public int LookupCodeOverrideId { get; set; }
    public int TenantId { get; set; }
    public int LookupCodeId { get; set; }
    public string? CustomLabelJson { get; set; }
    public string? CustomExtraJson { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int? SortOverride { get; set; }
    public LookupCode? LookupCode { get; set; }
}

/// <summary>Etapa de un pipeline; clasificada por StageKind (PIPELINE/LATERAL/TERMINAL). Sin grafo de workflow.</summary>
[AuditEntity(Constants.EntityTypes.StatusCode)]
public class StatusCode : ISoftDeletable
{
    public int StatusCodeId { get; set; }
    public string Entity { get; set; } = string.Empty;
    public string InternalCode { get; set; } = string.Empty;
    public string LabelJson { get; set; } = "{}";
    public string? DescriptionJson { get; set; }
    public string? ColorHex { get; set; }
    public string? Icon { get; set; }
    public int SortOrder { get; set; } = 100;
    public int StageKindLookupId { get; set; }
    public bool IsInitial { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public byte[]? RowVersion { get; set; }

    public CatalogDomain? Domain { get; set; }
    public LookupCode? StageKind { get; set; }
    public ICollection<StatusCodeOverride> Overrides { get; set; } = new List<StatusCodeOverride>();
}

[AuditEntity(Constants.EntityTypes.StatusConfig)]
public class StatusCodeOverride : ITenantScoped
{
    public int StatusCodeOverrideId { get; set; }
    public int TenantId { get; set; }
    public int StatusCodeId { get; set; }
    public string? CustomLabelJson { get; set; }
    public string? CustomColorHex { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int? SortOverride { get; set; }
    public StatusCode? StatusCode { get; set; }
}

/// <summary>Desde qué etapa del pipeline se puede saltar a un lateral. TenantId NULL = regla por defecto.</summary>
[AuditEntity(Constants.EntityTypes.StatusConfig)]
public class StatusLateralEntry : IOptionallyTenantScoped
{
    public int StatusLateralEntryId { get; set; }
    public int? TenantId { get; set; }
    public int EntityTypeLookupId { get; set; }
    public int LateralStatusCodeId { get; set; }
    public int FromStatusCodeId { get; set; }
    public bool IsAllowed { get; set; } = true;
    public LookupCode? EntityType { get; set; }
    public StatusCode? LateralStatus { get; set; }
    public StatusCode? FromStatus { get; set; }
}

/// <summary>Qué acción permite cada estatus (regla de negocio; RBAC es la otra compuerta).</summary>
[AuditEntity(Constants.EntityTypes.StatusConfig)]
public class StatusCapability : IOptionallyTenantScoped
{
    public int StatusCapabilityId { get; set; }
    public int? TenantId { get; set; }
    public int EntityTypeLookupId { get; set; }
    public int StatusCodeId { get; set; }
    public int CapabilityLookupId { get; set; }
    public bool IsAllowed { get; set; } = true;
    public LookupCode? EntityType { get; set; }
    public StatusCode? StatusCode { get; set; }
    public LookupCode? Capability { get; set; }
}

/// <summary>Plano 2 de auditoría: cómo se movió de estado.</summary>
public class EntityStatusHistory : ITenantScoped
{
    public long EntityStatusHistoryId { get; set; }
    public int TenantId { get; set; }
    public int EntityTypeLookupId { get; set; }
    public int EntityId { get; set; }
    public int? FromStatusCodeId { get; set; }
    public int ToStatusCodeId { get; set; }
    public string? Comment { get; set; }
    public DateTime ChangedAtUtc { get; set; } = DateTime.UtcNow;
    public int? ChangedBy { get; set; }
    public LookupCode? EntityType { get; set; }
    public StatusCode? FromStatus { get; set; }
    public StatusCode? ToStatus { get; set; }
}
