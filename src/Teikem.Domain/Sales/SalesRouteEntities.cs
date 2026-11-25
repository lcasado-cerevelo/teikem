using Teikem.Domain.Common;

namespace Teikem.Domain.Sales;

/// <summary>
/// Lote 244 (Lote A del plan de rutas) — constantes de las rutas de venta: personas de la ruta (titular y apoyo), cuota por ruta o por persona, aviso de ruta sin
/// titular y de dónde sale el vendedor de una orden. El EntityType SALES_ROUTE_ASSIGNMENT se siembra en la sección «Lote 244» de logistica-db-update.sql.
/// </summary>
public static class SalesRouteCodes
{
    /// <summary>EntityType de las personas de la ruta (auditoría y fuente de datos).</summary>
    public const string AssignmentEntityType = "SALES_ROUTE_ASSIGNMENT";
    /// <summary>Fuente de datos «Rutas de venta» (el EntityType SALES_ROUTE ya existe desde el Lote 241).</summary>
    public const string RouteDataSourceKey = SalesAnalysisCodes.RouteEntityType;
    /// <summary>Fuente de datos «Personas de la ruta» (titular y apoyo, con su historial).</summary>
    public const string AssignmentDataSourceKey = AssignmentEntityType;

    /// <summary>Papel de una persona en la ruta, como lo expone el API (en la base es IsPrimary).</summary>
    public const string RolePrimary = "PRIMARY";
    public const string RoleSupport = "SUPPORT";
    public static readonly string[] Roles = { RolePrimary, RoleSupport };

    /// <summary>Cómo se lleva la cuota (SalesAnalysisSetting.QuotaBy): por persona (representante, lo de siempre) o por ruta.</summary>
    public const string QuotaByRep = "REP";
    public const string QuotaByRoute = "ROUTE";
    public static readonly string[] QuotaByValues = { QuotaByRep, QuotaByRoute };

    /// <summary>Código del aviso de «Necesita tu atención»: ruta activa sin titular vigente (o con el titular inactivo).</summary>
    public const string NoPrimaryAttentionCode = "SALES_ROUTE_NO_PRIMARY";

    /// <summary>De dónde sale el vendedor propuesto de una orden: el preferido del cliente, el titular de la ruta o un apoyo de la ruta.</summary>
    public const string SellerKindPreferred = "PREFERRED";
    public const string SellerKindPrimary = "PRIMARY";
    public const string SellerKindSupport = "SUPPORT";
}

/// <summary>
/// Lote 244 — una persona (usuario de Teikem) en una ruta de venta, como titular (IsPrimary) o apoyo, con su vigencia en días de la compañía.
/// <para>
/// Vigente = IsActive y ValidTo NULL. El intervalo es medio abierto: [ValidFrom, ValidTo). No se programan asignaciones a futuro.
/// Nunca DELETE: cerrar = poner ValidTo (con ClosedAtUtc/ClosedBy); anular («registrada por error») = IsActive 0 y ValidTo ??= ValidFrom (con VoidedAtUtc/VoidedBy):
/// una anulada no cuenta en el historial ni en la atribución.
/// </para>
/// La base garantiza un solo titular vigente por ruta (UX_SalesRouteAssignment_Primary) y una sola fila vigente por persona y ruta (UX_SalesRouteAssignment_Open).
/// </summary>
[AuditEntity(SalesRouteCodes.AssignmentEntityType)]
public class SalesRouteAssignment : ITenantScoped, ISoftDeletable
{
    public int SalesRouteAssignmentId { get; set; }
    public int TenantId { get; set; }
    public int SalesRouteId { get; set; }
    public int UserId { get; set; }
    public bool IsPrimary { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public bool IsActive { get; set; } = true;
    [NotAudited] public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
    public int? ClosedBy { get; set; }
    public DateTime? VoidedAtUtc { get; set; }
    public int? VoidedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }
}
