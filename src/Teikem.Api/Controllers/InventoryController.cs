using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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
public sealed class InventoryController(InventoryReadService reads, InventoryAdjustmentService adjustments, TraceabilityService trace,
    InventoryReconciliationService reconciliation)
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
        [FromQuery] int skip = 0, [FromQuery] int take = 200, [FromQuery] bool activeProductsOnly = false)
        => reads.BalancesAsync(new BalanceQuery(NullIfEmpty(warehousePublicIds), NullIfEmpty(binIds), NullIfEmpty(productPublicIds),
            NullIfEmpty(categoryIds), lotNumber, includeZero, onlyAvailable, search, skip, take, activeProductsOnly), InventoryScope.Any, ct);

    /// <summary>
    /// Kárdex paginado (más recientes primero). quantity = la del ledger con signo (recepción +, despacho −);
    /// signedQuantity = perspectiva del filtro de almacenes/posiciones (salida −, entrada +, interna 0; sin filtro, una
    /// transferencia vale 0). Filtros: from/to (UTC), types, almacenes, posiciones, productos, categorías, lote, serie,
    /// refEntity + refId (documento de origen) y buscador. Lote 12: brands (marca del producto igual, sin distinguir
    /// mayúsculas) y name (el nombre del producto contiene el texto): los filtros de Productos para el Reporte de ajustes.
    /// Lote 14: from/to son días LOCALES de la compañía (hora de Puerto Rico); filtros nuevos: ownerClientPublicIds e
    /// includeOwn (dueño; cliente inexistente → 404 'Cliente no encontrado.'), reasons (motivos de ajuste; desconocido → 400),
    /// direction IN/OUT con la perspectiva de signedQuantity (otro → 400 'La dirección debe ser IN (entradas) u OUT (salidas).'),
    /// fromWarehousePublicIds y toWarehousePublicIds (cada uno contra su lado) y manualOnly (sin documento de referencia). La
    /// fila trae ownerName y categoryName.
    /// </summary>
    [HttpGet("transactions"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<KardexPageDto> Transactions([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string[]? types,
        [FromQuery] Guid[]? warehousePublicIds, [FromQuery] int[]? binIds, [FromQuery] Guid[]? productPublicIds,
        [FromQuery] int[]? categoryIds, [FromQuery] string? lotNumber, [FromQuery] string? serialNumber,
        [FromQuery] string? refEntity, [FromQuery] int? refId, [FromQuery] string? search, [FromQuery] string[]? brands,
        [FromQuery] string? name, CancellationToken ct, [FromQuery] int skip = 0, [FromQuery] int take = 200,
        [FromQuery] Guid[]? ownerClientPublicIds = null, [FromQuery] bool includeOwn = false, [FromQuery] string[]? reasons = null,
        [FromQuery] string? direction = null, [FromQuery] Guid[]? fromWarehousePublicIds = null, [FromQuery] Guid[]? toWarehousePublicIds = null,
        [FromQuery] bool manualOnly = false)
        => reads.KardexAsync(new KardexQuery(from, to, NullIfEmpty(types), NullIfEmpty(warehousePublicIds), NullIfEmpty(binIds),
            NullIfEmpty(productPublicIds), NullIfEmpty(categoryIds), lotNumber, serialNumber, refEntity, refId, search, skip, take,
            NullIfEmpty(brands), name, NullIfEmpty(ownerClientPublicIds), includeOwn, NullIfEmpty(reasons), direction,
            NullIfEmpty(fromWarehousePublicIds), NullIfEmpty(toWarehousePublicIds), manualOnly), InventoryScope.Any, ct);

    /// <summary>
    /// Lote 14 (D13) — resumen del Kárdex con LOS MISMOS filtros de la lista (sin skip/take): movimientos, entradas (número y
    /// unidades), salidas (número y unidades) e internos, con la perspectiva de signedQuantity (sin filtro de ubicación una
    /// transferencia es interna). En Saldos, "En mano" y "Disponible" salen de GET /inventory/balances (totalOnHand, totalAvailable).
    /// </summary>
    [HttpGet("transactions/summary"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<KardexSummaryDto> TransactionsSummary([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string[]? types,
        [FromQuery] Guid[]? warehousePublicIds, [FromQuery] int[]? binIds, [FromQuery] Guid[]? productPublicIds,
        [FromQuery] int[]? categoryIds, [FromQuery] string? lotNumber, [FromQuery] string? serialNumber,
        [FromQuery] string? refEntity, [FromQuery] int? refId, [FromQuery] string? search, [FromQuery] string[]? brands,
        [FromQuery] string? name, [FromQuery] Guid[]? ownerClientPublicIds, [FromQuery] string[]? reasons, [FromQuery] string? direction,
        [FromQuery] Guid[]? fromWarehousePublicIds, [FromQuery] Guid[]? toWarehousePublicIds, CancellationToken ct,
        [FromQuery] bool includeOwn = false, [FromQuery] bool manualOnly = false)
        => reads.KardexSummaryAsync(new KardexQuery(from, to, NullIfEmpty(types), NullIfEmpty(warehousePublicIds), NullIfEmpty(binIds),
            NullIfEmpty(productPublicIds), NullIfEmpty(categoryIds), lotNumber, serialNumber, refEntity, refId, search, 0, 0,
            NullIfEmpty(brands), name, NullIfEmpty(ownerClientPublicIds), includeOwn, NullIfEmpty(reasons), direction,
            NullIfEmpty(fromWarehousePublicIds), NullIfEmpty(toWarehousePublicIds), manualOnly), InventoryScope.Any, ct);

    /// <summary>
    /// Lote 14 — detalle de un movimiento: la fila con dueño y categoría, vencimiento del lote, documento de origen (con su
    /// PublicId para abrir recibos, recolecciones, órdenes de compra y órdenes; el conteo por id; la tarea con su documento
    /// padre) y los movimientos relacionados (misma referencia o mismo asiento; tope 200). 404 'Movimiento no encontrado.'.
    /// </summary>
    [HttpGet("transactions/{id:long}"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<KardexDetailDto> Transaction(long id, CancellationToken ct) => reads.TransactionDetailAsync(id, InventoryScope.Any, ct);

    /// <summary>Lote 14 — dueños del inventario para el filtro: "Propio" (isOwn) y los clientes dueños de algún producto.</summary>
    [HttpGet("owners"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<IReadOnlyList<InventoryOwnerDto>> Owners(CancellationToken ct) => reads.OwnersAsync(InventoryScope.Any, ct);

    /// <summary>
    /// Ajuste manual: quantity con signo (&gt; 0 entra a la posición, &lt; 0 sale; 0 → 400) y motivo del catálogo
    /// AdjustmentReason (RECEIPT_VARIANCE, COUNT_VARIANCE, PICK_BATCH_REVERSAL y OPENING_BALANCE los asigna el sistema → 400).
    /// notes obligatoria (vacía → 400 errors.notes 'Escriba una nota que explique el ajuste.'; máx. 300).
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

    /// <summary>
    /// Lote 14 — "Ejecutar conciliación": revisa los productos indicados (≤ 200; más → 400 'La conciliación manual admite como
    /// máximo 200 productos a la vez.') o todo el tenant y GUARDA los descuadres (origen MANUAL): abre los nuevos, actualiza los
    /// abiertos y cierra solos los que ya cuadran. Devuelve cuántos y las filas descuadradas.
    /// </summary>
    [HttpPost("reconciliation/run"), RequirePermission(PermissionCatalog.InventoryAdjust)]
    public Task<ReconciliationRunDto> RunReconciliation([FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReconciliationRunRequest? req,
        CancellationToken ct)
        => reconciliation.RunAsync(req, ct);

    /// <summary>
    /// Lote 14 (D14) — estado de la revisión automática en segundo plano (segundos después de cada movimiento) para la compañía:
    /// encendida, consumiendo, pendientes (0 = al día), revisados, descartados por cola llena, última revisión y último error.
    /// Contadores en memoria desde que arrancó el servidor.
    /// </summary>
    [HttpGet("reconciliation/status"), RequirePermission(PermissionCatalog.InventoryAdjust)]
    public ReconciliationStatusDto ReconciliationStatus() => reconciliation.Status();

    private static T[]? NullIfEmpty<T>(T[]? values) => values is { Length: > 0 } ? values : null;
}
