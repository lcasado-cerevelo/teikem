using Teikem.Infrastructure.Contracts;

namespace Teikem.Infrastructure.Abstractions;

/// <summary>Contexto de lectura de un proveedor de actividad: tenant, usuario, idioma y módulos del tenant encendidos.</summary>
public sealed record ActivityScope(int TenantId, int? UserId, string Lang, IReadOnlySet<string> EnabledModules);

/// <summary>
/// Lote 7A — proveedor de eventos de "Actividad reciente" para un BusinessModule (una pestaña del panel). Se registra en DI
/// como colección, igual que IDataSource; ActivityFeedService muestra la pestaña si el usuario tiene RequiredPermission.
/// Cada proveedor define en código qué transición (EntityStatusHistory), movimiento (ledger) o cambio auditado (AuditLog)
/// produce cada código del catálogo ActivityEventType; no hay tabla de eventos.
/// </summary>
public interface IActivityEventProvider
{
    /// <summary>Código BusinessModule de la pestaña (WAREHOUSE, OPERATIONS, ACCOUNTING).</summary>
    string Module { get; }

    /// <summary>Permiso que da visibilidad a la pestaña (Almacén: inventory.view).</summary>
    string RequiredPermission { get; }

    /// <summary>Eventos del tenant del scope desde fromUtc (sin paginar; el servicio ordena y recorta).</summary>
    Task<IReadOnlyList<ActivityEventDto>> ReadAsync(ActivityScope scope, DateTime fromUtc, bool onlyMandatory, CancellationToken ct);
}
