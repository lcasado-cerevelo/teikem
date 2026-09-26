using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 6 (P3) — inventario (módulo WMS_LOTSERIAL): saldos por almacén/posición/lote, Kárdex de solo lectura con la
/// cantidad CON signo del ledger (D3, L331), ajuste manual ± con motivo de catálogo, transferencias entre posiciones o
/// almacenes (una fila TRANSFER, D40), genealogía de lote, rastro de serie y conciliación ledger ↔ saldo.
/// - Lecturas con inventory.view; ajustes, transferencias y conciliación con inventory.adjust.
/// - Todas las lecturas pasan InventoryScope.Any (usuarios internos del tenant); el Portal (Lote 8) pasará el cliente
///   dueño (D44). Ninguna solicitud lleva TenantId: sale del principal.
/// - Almacenes y productos por PublicId; posiciones y lotes por id entero, resueltos SIEMPRE a través de su padre
///   filtrado (una posición de otro almacén u otro tenant → 404).
/// - Fechas del Kárdex en UTC: from inclusivo, to inclusivo (el servicio lo vuelve exclusivo sumando un día).
/// </summary>
[ApiController]
[Route("api/v1/inventory")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class InventoryController(InventoryReadService reads, InventoryAdjustmentService adjustments, TraceabilityService trace)
    : ControllerBase
{
    /// <summary>
    /// Saldos paginados (take ≤ 200) con filtros múltiples: almacenes, posiciones, productos, categorías (con sus
    /// subcategorías), número de lote, includeZero (también saldos en cero), onlyAvailable (disponible &gt; 0) y buscador
    /// (SKU, nombre, código de barras, lote o posición). El disponible = en mano − reservado.
    /// </summary>
    [HttpGet("balances"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<BalancePageDto> Balances([FromQuery] Guid[]? warehousePublicIds, [FromQuery] int[]? binIds,
        [FromQuery] Guid[]? productPublicIds, [FromQuery] int[]? categoryIds, [FromQuery] string? lotNumber,
        [FromQuery] bool includeZero, [FromQuery] bool onlyAvailable, [FromQuery] string? search, CancellationToken ct,
        [FromQuery] int skip = 0, [FromQuery] int take = 200)
        => reads.BalancesAsync(new BalanceQuery(NullIfEmpty(warehousePublicIds), NullIfEmpty(binIds), NullIfEmpty(productPublicIds),
            NullIfEmpty(categoryIds), lotNumber, includeZero, onlyAvailable, search, skip, take), InventoryScope.Any, ct);

    /// <summary>
    /// Kárdex paginado (más recientes primero). quantity = la del ledger con signo (recepción +, despacho −);
    /// signedQuantity = perspectiva del filtro de almacenes/posiciones (salida −, entrada +, interna 0; sin filtro, una
    /// transferencia vale 0). Filtros: from/to (UTC), types, almacenes, posiciones, productos, categorías, lote, serie,
    /// refEntity + refId (documento de origen) y buscador.
    /// </summary>
    [HttpGet("transactions"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<KardexPageDto> Transactions([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string[]? types,
        [FromQuery] Guid[]? warehousePublicIds, [FromQuery] int[]? binIds, [FromQuery] Guid[]? productPublicIds,
        [FromQuery] int[]? categoryIds, [FromQuery] string? lotNumber, [FromQuery] string? serialNumber,
        [FromQuery] string? refEntity, [FromQuery] int? refId, [FromQuery] string? search, CancellationToken ct,
        [FromQuery] int skip = 0, [FromQuery] int take = 200)
        => reads.KardexAsync(new KardexQuery(from, to, NullIfEmpty(types), NullIfEmpty(warehousePublicIds), NullIfEmpty(binIds),
            NullIfEmpty(productPublicIds), NullIfEmpty(categoryIds), lotNumber, serialNumber, refEntity, refId, search, skip, take),
            InventoryScope.Any, ct);

    /// <summary>
    /// Ajuste manual: quantity con signo (&gt; 0 entra a la posición, &lt; 0 sale; 0 → 400) y motivo del catálogo
    /// AdjustmentReason (RECEIPT_VARIANCE, COUNT_VARIANCE y PICK_BATCH_REVERSAL los asigna el sistema → 400).
    /// Salida por encima del disponible → 409 insufficient_stock.
    /// </summary>
    [HttpPost("adjustments"), RequirePermission(PermissionCatalog.InventoryAdjust)]
    public Task<MovementResultDto> Adjust([FromBody] AdjustmentRequest req, CancellationToken ct) => adjustments.AdjustAsync(req, ct);

    /// <summary>
    /// Transferencia entre posiciones del mismo almacén o entre almacenes (una sola fila TRANSFER, instantánea).
    /// Misma posición → 400; más que el disponible (lo reservado no se mueve) → 409 insufficient_stock.
    /// </summary>
    [HttpPost("transfers"), RequirePermission(PermissionCatalog.InventoryAdjust)]
    public Task<MovementResultDto> Transfer([FromBody] TransferRequest req, CancellationToken ct) => adjustments.TransferAsync(req, ct);

    /// <summary>Genealogía de un lote: movimientos, entradas/salidas/existencia y destinos (orden, cliente, consignatario).</summary>
    [HttpGet("lots/{lotId:int}/genealogy"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<GenealogyDto> Genealogy(int lotId, CancellationToken ct) => trace.LotGenealogyAsync(lotId, InventoryScope.Any, ct);

    /// <summary>Rastro de una serie del producto: ficha actual, movimientos del ledger e historial de estatus.</summary>
    [HttpGet("serials/trace"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<SerialTraceDto> SerialTrace([FromQuery] Guid productPublicId, [FromQuery] string? serialNumber, CancellationToken ct)
        => trace.SerialTraceAsync(productPublicId, serialNumber ?? string.Empty, InventoryScope.Any, ct);

    /// <summary>Conciliación ledger ↔ saldo (todo el tenant o un producto): mismatches vacío = el invariante se cumple.</summary>
    [HttpGet("reconciliation"), RequirePermission(PermissionCatalog.InventoryAdjust)]
    public Task<ReconciliationDto> Reconciliation([FromQuery] Guid? productPublicId, CancellationToken ct)
        => trace.ReconcileAsync(productPublicId, ct);

    private static T[]? NullIfEmpty<T>(T[]? values) => values is { Length: > 0 } ? values : null;
}
