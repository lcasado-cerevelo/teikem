using Teikem.Domain.Common;

namespace Teikem.Domain.Fleet;

/// <summary>
/// Zona de despacho del tenant ('R-01'): territorio fijo que un chofer cubre habitualmente. Código único por compañía
/// (UQ_DispatchZone) e inmutable; Name es el texto de los pueblos que se muestra como 'Área' del chofer. Los miembros
/// (DispatchZoneMember: CP/municipios) son de Despacho y no se mapean en este lote.
/// </summary>
[AuditEntity(Constants.EntityTypes.DispatchZone)]
public class DispatchZone : ITenantScoped, ISoftDeletable
{
    public int DispatchZoneId { get; set; }
    public int TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Name { get; set; }
    public bool IsActive { get; set; } = true;
}
