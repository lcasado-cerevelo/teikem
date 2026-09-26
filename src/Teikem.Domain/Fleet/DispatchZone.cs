using Teikem.Domain.Common;

namespace Teikem.Domain.Fleet;

/// <summary>
/// Zona de despacho del tenant ('R-01'): territorio fijo que un chofer cubre habitualmente. Código único por compañía
/// (UQ_DispatchZone) e inmutable; Name es el texto de los pueblos que se muestra como 'Área' del chofer.
/// Lote 5: sus miembros (DispatchZoneMember: código postal, rango postal o municipio) resuelven 'ZIP/pueblo → zona'
/// para Despacho, 'Planificar el día' y el escaneo Outbound.
/// </summary>
[AuditEntity(Constants.EntityTypes.DispatchZone)]
public class DispatchZone : ITenantScoped, ISoftDeletable
{
    public int DispatchZoneId { get; set; }
    public int TenantId { get; set; }
    public string Code { get; set; } = string.Empty;
    public string? Name { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<DispatchZoneMember> Members { get; set; } = new List<DispatchZoneMember>();
}

/// <summary>
/// Lote 5 — Criterio de pertenencia a una zona de despacho: MatchTypeLookupId (ZoneMatchType: POSTAL_CODE, POSTAL_RANGE,
/// MUNICIPALITY; POLYGON no se soporta aún) y MatchValue normalizado (DispatchZoneMatcher.NormalizeMember). Sin TenantId:
/// se alcanza SOLO por una zona ya filtrada (BOLA por id hijo). Quitarlo es un DELETE físico auditado bajo DISPATCH_ZONE.
/// Repetido en la misma zona: UQ_DispatchZoneMember; solapamiento con otra zona activa: lo impide el servicio.
/// </summary>
[AuditEntity(Constants.EntityTypes.DispatchZone)]
public class DispatchZoneMember
{
    public int DispatchZoneMemberId { get; set; }
    public int DispatchZoneId { get; set; }
    public int MatchTypeLookupId { get; set; }
    public string MatchValue { get; set; } = string.Empty;

    public DispatchZone? DispatchZone { get; set; }
}
