using Teikem.Domain.Clients;
using Teikem.Domain.Common;

namespace Teikem.Domain.Orders;

/// <summary>
/// Plantilla de importación por posición de columna (Lote 3, ajuste D; misma idea que CTPL-1 de consignatarios).
/// ClientId NULL = plantilla general del tenant. ColumnsJson = [{position, field}] con field del catálogo ImportFields;
/// DefaultsJson = {campo: valor} (valores fijos por campo, p. ej. serviceType STANDARD). Nombre único por (Tenant, Kind).
/// Baja lógica (IsActive=0), nunca DELETE: los lotes ya validados conservan la referencia.
/// </summary>
[AuditEntity(Constants.EntityTypes.ImportTemplate)]
public class ImportTemplate : ITenantScoped, ISoftDeletable
{
    public int ImportTemplateId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    /// <summary>ImportKinds: ORDER.</summary>
    public string Kind { get; set; } = ImportKinds.Order;
    public string Name { get; set; } = string.Empty;
    public int? ClientId { get; set; }
    /// <summary>Un carácter (NCHAR(1)); ',' por defecto.</summary>
    public string Delimiter { get; set; } = ",";
    public bool HasHeader { get; set; } = true;
    public string ColumnsJson { get; set; } = "[]";
    public string? DefaultsJson { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }

    public Client? Client { get; set; }
}
