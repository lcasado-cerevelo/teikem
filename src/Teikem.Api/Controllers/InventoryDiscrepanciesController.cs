using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 14 (P1, D5) — descuadres Kárdex ↔ saldo (módulo WMS_LOTSERIAL). Los ve quien ve inventario (inventory.view); los
/// resuelve quien puede ajustar (inventory.adjust): corregir el saldo según el Kárdex (REBUILD_BALANCE) o descartar con nota
/// (DISMISS). Se exponen por PublicId; el TenantId sale del principal (otro tenant → 404 'Descuadre no encontrado.').
/// Historial de estatus también en /api/v1/status/history/INVENTORY_DISCREPANCY/{id}.
/// </summary>
[ApiController]
[Route("api/v1/inventory/discrepancies")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class InventoryDiscrepanciesController(InventoryReconciliationService reconciliation) : ControllerBase
{
    /// <summary>
    /// Descuadres paginados (take ≤ 200, por defecto 50; abiertos primero). Filtros: status (OPEN, RESOLVED, DISMISSED,
    /// SELF_CORRECTED), almacenes, productos, categorías (con subcategorías), posiciones, kinds (BALANCE, PRODUCT_TOTAL; otro →
    /// 400) y from/to de detección (días locales). openCount = pendientes con los mismos filtros salvo el estatus.
    /// </summary>
    [HttpGet, RequirePermission(PermissionCatalog.InventoryView)]
    public Task<InventoryDiscrepancyPageDto> List([FromQuery] string[]? status, [FromQuery] Guid[]? warehousePublicIds,
        [FromQuery] Guid[]? productPublicIds, [FromQuery] int[]? categoryIds, [FromQuery] int[]? binIds, [FromQuery] string[]? kinds,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct, [FromQuery] int skip = 0, [FromQuery] int take = 50)
        => reconciliation.ListAsync(new InventoryDiscrepancyQuery(NullIfEmpty(status), NullIfEmpty(warehousePublicIds), NullIfEmpty(productPublicIds),
            NullIfEmpty(categoryIds), NullIfEmpty(binIds), NullIfEmpty(kinds), from, to, skip, take), ct);

    /// <summary>Ficha: el descuadre, lo reservado hoy, los últimos 20 movimientos de la clave y el historial de estatus.</summary>
    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<InventoryDiscrepancyDetailDto> Get(Guid publicId, CancellationToken ct) => reconciliation.GetAsync(publicId, ct);

    /// <summary>
    /// Resolver { action, notes, rowVersion }: REBUILD_BALANCE (el saldo toma lo que da el Kárdex; si ya cuadraba se cierra
    /// solo) o DISMISS (nota obligatoria). 400 acción/nota; 409 rowVersion o Kárdex negativo / menor que lo reservado; 422
    /// cerrado o REBUILD sobre el total del producto.
    /// </summary>
    [HttpPost("{publicId:guid}/resolve"), RequirePermission(PermissionCatalog.InventoryAdjust)]
    public Task<InventoryDiscrepancyDetailDto> Resolve(Guid publicId, [FromBody] DiscrepancyResolveRequest req, CancellationToken ct)
        => reconciliation.ResolveAsync(publicId, req, ct);

    private static T[]? NullIfEmpty<T>(T[]? values) => values is { Length: > 0 } ? values : null;
}
