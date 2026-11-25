using Teikem.Domain.Common;

namespace Teikem.Domain.Clients;

/// <summary>
/// Lote 245 (Lote B del plan de rutas, cadenas y precios) — constantes de las cadenas de clientes. Viven aquí (y no en los catálogos compartidos) para que el lote se
/// pueda pasar solo a otra rama. El EntityType se siembra en la sección «2026-11-25 — Lote 245: Cadenas de clientes» de logistica-db-update.sql.
/// </summary>
public static class ClientChainCodes
{
    /// <summary>EntityType de la bitácora y clave de la fuente de datos «Cadenas de clientes».</summary>
    public const string EntityType = "CLIENT_CHAIN";
    /// <summary>Fuente de datos de Vistas, Indicadores y Gráficos (misma clave que el EntityType).</summary>
    public const string DataSourceKey = EntityType;

    /// <summary>Qué le pasa a un cliente en una asignación o en el alta masiva por nombre.</summary>
    /// <remarks>ASSIGN = no tenía cadena y entra; MOVE = estaba en otra y se mueve (solo si se pidió mover); ALREADY = ya estaba en esta;
    /// SKIP_OTHER_CHAIN = está en otra y no se pidió mover (se reporta, no es error); EXCLUDED = se quitó a mano de la simulación.</remarks>
    public const string ActionAssign = "ASSIGN";
    public const string ActionMove = "MOVE";
    public const string ActionAlready = "ALREADY";
    public const string ActionSkipOtherChain = "SKIP_OTHER_CHAIN";
    public const string ActionExcluded = "EXCLUDED";
    public static readonly string[] Actions = { ActionAssign, ActionMove, ActionAlready, ActionSkipOtherChain, ActionExcluded };

    /// <summary>Renglón de los clientes sin cadena (análisis por cadena y campos ChainName de las fuentes de datos).</summary>
    public const string NoChainName = "Sin cadena";
}

/// <summary>
/// Lote 245 — cadena de clientes (Walgreens, CVS…): agrupa clientes para ver su venta junta y, desde el Lote C, para precios y ofertas por cadena. Un cliente
/// pertenece a UNA sola cadena (<see cref="Client.ChainId"/>). Sin código: el nombre es único entre las ACTIVAS de la compañía. No lleva estatus: «activa» =
/// IsActive (como la ruta de venta); desactivar exige que no tenga clientes activos. Una cadena NO es una ruta ni cambia la ruta del cliente. Nunca DELETE.
/// <para>
/// UpdatedAtUtc y UpdatedBy no se auditan: cada asignación «toca» la cadena (cambia UpdatedAtUtc) para que el UPDATE viaje con RowVersion y una desactivación
/// concurrente pierda con 409; sin [NotAudited] ese toque dejaría bitácora vacía. Nombre, notas y activa sí se auditan.
/// </para>
/// </summary>
[AuditEntity(ClientChainCodes.EntityType)]
public class ClientChain : ITenantScoped, ISoftDeletable, IAuditStamped
{
    public int ClientChainId { get; set; }
    [NotAudited] public Guid PublicId { get; set; }
    public int TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; }
    public int? CreatedBy { get; set; }
    [NotAudited] public DateTime? UpdatedAtUtc { get; set; }
    [NotAudited] public int? UpdatedBy { get; set; }
    [NotAudited] public byte[]? RowVersion { get; set; }
}
