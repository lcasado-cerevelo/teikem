namespace Teikem.Domain.Common;

/// <summary>Entidad que pertenece obligatoriamente a un tenant. El DbContext le aplica filtro global.</summary>
public interface ITenantScoped
{
    int TenantId { get; set; }
}

/// <summary>Entidad con TenantId opcional: NULL = fila global/plantilla del sistema, visible para todos los tenants.</summary>
public interface IOptionallyTenantScoped
{
    int? TenantId { get; set; }
}

/// <summary>Soft-delete estándar de la plataforma (IsActive = 0 en vez de DELETE).</summary>
public interface ISoftDeletable
{
    bool IsActive { get; set; }
}

/// <summary>Entidad con estatus del motor de estatus (StatusCode).</summary>
public interface IHasStatus
{
    int StatusCodeId { get; set; }
}

/// <summary>Auditoría inherente: quién y cuándo creó/actualizó.</summary>
public interface IAuditStamped
{
    DateTime CreatedAtUtc { get; set; }
    int? CreatedBy { get; set; }
    DateTime? UpdatedAtUtc { get; set; }
    int? UpdatedBy { get; set; }
}
