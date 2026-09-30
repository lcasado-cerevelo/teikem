using Teikem.Infrastructure.Contracts;

namespace Teikem.Infrastructure.Abstractions;

/// <summary>Contexto de lectura de un proveedor de "Necesita tu atención": tenant, usuario, idioma y módulos encendidos.</summary>
public sealed record AttentionScope(int TenantId, int? UserId, string Lang, IReadOnlySet<string> EnabledModules);

/// <summary>Lo que aporta un proveedor: su total de pendientes, sus filas (las más antiguas, hasta take) y su "Ver todos".</summary>
public sealed record AttentionProviderResult(int Total, IReadOnlyList<AttentionItemDto> Items, AttentionGroupDto? Group);

/// <summary>
/// Lote 14 (D6) — proveedor de filas del panel "Necesita tu atención" del Pulso, uno por tipo de aviso (espejo de
/// IActivityEventProvider). Se registra en DI como colección; AttentionFeedService lo consulta solo si el módulo del tenant
/// está encendido y el usuario tiene RequiredPermission. Hoy: descuadres de inventario (WMS_LOTSERIAL + inventory.view);
/// Operación y COD podrán sumar los suyos sin tocar el panel.
/// </summary>
public interface IAttentionItemProvider
{
    /// <summary>Código del tipo de aviso (clave de i18n en la web), p. ej. INVENTORY_DISCREPANCY.</summary>
    string Code { get; }

    /// <summary>Módulo de negocio (WAREHOUSE, OPERATIONS, ACCOUNTING): ícono y orden de desempate.</summary>
    string BusinessModule { get; }

    /// <summary>Módulo del tenant que debe estar encendido (ModuleKeys); null = sin requisito.</summary>
    string? TenantModule { get; }

    /// <summary>Permiso que da visibilidad a las filas del proveedor.</summary>
    string RequiredPermission { get; }

    /// <summary>Pendientes del tenant del scope: total y las filas más antiguas (hasta take).</summary>
    Task<AttentionProviderResult> ReadAsync(AttentionScope scope, int take, CancellationToken ct);
}
