using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;

namespace Teikem.Infrastructure.Services;

/// <summary>
/// Lote 14 (D6) — panel "Necesita tu atención" del Pulso (GET /api/v1/analytics/attention, pulse.attention). No hay tabla de
/// avisos: cada proveedor (IAttentionItemProvider) calcula sus pendientes al leer.
/// - Proveedores visibles = módulo del tenant encendido + permiso del proveedor (descuadres: WMS_LOTSERIAL + inventory.view).
///   Uno no visible no aporta filas ni total (sin 403: el panel es una suma de lo que el usuario puede ver).
/// - Items = las 5 filas más antiguas de todos los proveedores (SinceUtc ascendente; sin fecha al final); Total = suma de los
///   pendientes (para "Ver todos (N)"); Groups = total y ruta por tipo. Nada pendiente → Total 0 y listas vacías.
/// </summary>
public sealed class AttentionFeedService(
    IEnumerable<IAttentionItemProvider> providers,
    ITenantContext tenant,
    PermissionService permissions,
    ModuleService modules)
{
    /// <summary>Filas del panel (D6: los 5 más antiguos).</summary>
    public const int MaxItems = 5;

    public async Task<AttentionDto> GetAsync(CancellationToken ct)
    {
        var tenantId = tenant.TenantId ?? throw new ForbiddenException("No hay tenant activo en la sesión.");
        var enabled = await modules.GetEnabledKeysAsync(tenantId, ct);
        var scope = new AttentionScope(tenantId, tenant.UserId, tenant.Lang, enabled);

        var total = 0;
        var items = new List<AttentionItemDto>();
        var groups = new List<AttentionGroupDto>();
        foreach (var p in providers.OrderBy(p => ActivityRules.ModuleRank(p.BusinessModule)).ThenBy(p => p.Code, StringComparer.Ordinal))
        {
            if (p.TenantModule is string key && !enabled.Contains(key)) continue;
            if (!await permissions.HasPermissionAsync(p.RequiredPermission, ct)) continue;
            var result = await p.ReadAsync(scope, MaxItems, ct);
            if (result.Total <= 0) continue;
            total += result.Total;
            items.AddRange(result.Items);
            if (result.Group is not null) groups.Add(result.Group);
        }
        return new AttentionDto(total, Oldest(items, MaxItems), groups);
    }

    /// <summary>Las filas más antiguas primero (SinceUtc ascendente; sin fecha al final; desempate por código), hasta max.</summary>
    public static IReadOnlyList<AttentionItemDto> Oldest(IEnumerable<AttentionItemDto> items, int max)
        => items.OrderBy(i => i.SinceUtc is null ? 1 : 0).ThenBy(i => i.SinceUtc).ThenBy(i => i.Code, StringComparer.Ordinal)
            .Take(Math.Max(0, max)).ToList();
}
