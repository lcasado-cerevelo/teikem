using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 6 (P7) — Recolección y empaque ad hoc (R13, R34-R43) bajo el módulo WMS_LOTSERIAL. Lectura con inventory.view;
/// recolectar, empacar y eliminar con warehouse.pick. Empacar exige ADEMÁS orders.create y eliminar una recolección
/// empacada exige orders.cancel (los verifica el servicio: 403 con PERMISSION_DENIED).
/// La recolección se expone por PublicId; su historial de estatus: /api/v1/status/history/PICK_BATCH/{id}.
/// </summary>
[ApiController]
[Route("api/v1/pick-batches")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class PickBatchesController(PickBatchService batches) : ControllerBase
{
    /// <summary>
    /// Lista paginada (take ≤ 200), de la más reciente a la más antigua. Filtros: from/to (fecha de recolección en UTC,
    /// 'hasta' inclusive), productPublicIds, status (COLLECTED, PACKED, CANCELLED), orderNumber, invoiceNumber e
    /// includeDeleted (las eliminadas se excluyen por defecto, D39). La búsqueda (search: número, empaque, orden, factura,
    /// cliente, motivo o nota del despacho manual o SKU) se aplica al final, sobre lo ya filtrado. 2026-10-11: kind = MANUAL
    /// (solo despachos manuales DMA), PACK (solo recolecciones EMP) o ALL (por omisión); otro valor → 400.
    /// </summary>
    [HttpGet, RequirePermission(PermissionCatalog.InventoryView)]
    public Task<PickBatchPageDto> List([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid[]? productPublicIds,
        [FromQuery] string[]? status, [FromQuery] string? orderNumber, [FromQuery] string? invoiceNumber, [FromQuery] string? search,
        [FromQuery] bool includeDeleted = false, [FromQuery] int skip = 0, [FromQuery] int take = 100, [FromQuery] string? kind = null,
        CancellationToken ct = default)
        => batches.ListAsync(new PickBatchQuery(from, to, productPublicIds is { Length: > 0 } ? productPublicIds : null,
            status is { Length: > 0 } ? status : null, orderNumber, invoiceNumber, search, includeDeleted, skip, take, kind), ct);

    /// <summary>Ficha: líneas (posición, lote, serie, costo congelado, movimiento y reversa), orden del empaque y acciones posibles.</summary>
    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<PickBatchDto> Get(Guid publicId, CancellationToken ct) => batches.GetAsync(publicId, ct);

    /// <summary>
    /// Recolecta (COLLECTED, EMP-#####): productos activos de un solo dueño; posición y lote por FEFO o explícitos; los
    /// productos con serie se recolectan escaneando la serie. Saca el inventario con un ISSUE por porción. Inventario
    /// insuficiente → 409 insufficient_stock sin efecto parcial (el número tampoco se consume). Con 'pack' en el cuerpo → 400
    /// (use collect-and-pack).
    /// </summary>
    [HttpPost, RequirePermission(PermissionCatalog.WarehousePick)]
    public Task<PickBatchDto> Collect([FromBody] PickBatchCreateRequest req, CancellationToken ct)
        => batches.CollectAsync(req, ct);

    /// <summary>
    /// Lote 8A (cola del aparato): recolecta y empaca en UNA transacción (+ orders.create, mismas reglas y mensajes que
    /// POST / y POST /{publicId}/pack) y devuelve { batch, order }. 'pack' es obligatorio (sin él → 400 'pack'); si el
    /// empaque falla tampoco queda la recolección (ni sale inventario ni se consume el número). Respeta Idempotency-Key.
    /// </summary>
    [HttpPost("collect-and-pack"), RequirePermission(PermissionCatalog.WarehousePick)]
    public Task<PickBatchPackResultDto> CollectAndPack([FromBody] PickBatchCreateRequest req, CancellationToken ct)
        => batches.CollectAndPackAsync(req, ct);

    /// <summary>
    /// Empaca: crea la orden real (número de empaque = número de la recolección; + orders.create) y pasa a PACKED. Sin entrega
    /// especial ni chofer; con inventario de un cliente 3PL la orden debe ser de ese cliente. Ya empacada → 422. Un despacho
    /// manual (DMA) → 422 (no se empaca).
    /// </summary>
    [HttpPost("{publicId:guid}/pack"), RequirePermission(PermissionCatalog.WarehousePick)]
    public Task<PickBatchPackResultDto> Pack(Guid publicId, [FromBody] PickBatchPackRequest req, CancellationToken ct)
        => batches.PackAsync(publicId, req, ct);

    /// <summary>
    /// Elimina (204): restaura el inventario a la posición original (ADJUSTMENT PICK_BATCH_REVERSAL) y pasa a CANCELLED. Si
    /// está empacada (+ orders.cancel) elimina también su orden, solo si sigue en la etapa inicial (si no, 422). Un despacho
    /// manual exige además warehouse.issue (lo verifica el servicio).
    /// </summary>
    [HttpDelete("{publicId:guid}"), RequirePermission(PermissionCatalog.WarehousePick)]
    public async Task<IActionResult> Delete(Guid publicId, [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PickBatchDeleteRequest? req,
        CancellationToken ct)
    {
        await batches.DeleteAsync(publicId, req, ct);
        return NoContent();
    }
}
