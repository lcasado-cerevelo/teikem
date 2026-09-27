using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 8A (P2) — sincronización por diferencia para la app de almacén (módulo WMS_LOTSERIAL, inventory.view; las órdenes de
/// compra exigen además el módulo PURCHASING y purchasing.view, como su recurso nativo). Cada recurso
/// se baja a la base local del aparato en páginas de hasta 500 (take ≤ 500; más → 400 'El máximo por página es 500.').
/// <para>Protocolo del aparato, por tabla:</para>
/// <list type="number">
/// <item>Primera vez: sin since → carga completa de lo vigente (activo / abierto).</item>
/// <item>Paginar con cursor = nextCursor de la respuesta anterior (opaco: se devuelve tal cual) hasta que nextCursor sea null,
/// SIN cambiar since ni los filtros entre páginas.</item>
/// <item>Guardar como marca de agua el serverTimeUtc de la PRIMERA página de la pasada y, en la siguiente pasada, pedir
/// since = ese serverTimeUtc MENOS 5 MINUTOS (margen para relojes y transacciones en curso; lo repetido se sobrescribe por id).</item>
/// <item>Con since llegan también las filas dadas de baja, cerradas o canceladas con isActive = false: el aparato las borra.</item>
/// </list>
/// Un cursor que no salió de este API → 400 'El cursor no es válido.'. Un almacén de otro tenant o inexistente → 404.
/// <para>Criterio de since (decisión a revisar, docs/lote8A-decisiones.md): estas tablas no tienen UpdatedAtUtc; "cambió
/// desde since" = tiene AuditLog (interceptor de auditoría) o EntityStatusHistory de su EntityType desde since, o nació
/// (CreatedAtUtc) o se cerró (CompletedAtUtc) desde entonces. Puede devolver de más (las hijas se auditan bajo el tipo del
/// padre; las tareas abiertas llegan siempre), nunca de menos respecto de lo que pasa por el interceptor; un cambio hecho
/// fuera de él (ExecuteUpdate o SQL directo) no se detecta hasta la siguiente carga completa.</para>
/// </summary>
[ApiController]
[Route("api/v1/sync")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
public sealed class SyncController(SyncService sync) : ControllerBase
{

    /// <summary>
    /// Productos (id, publicId, SKU, nombre, código de barras, seguimiento, unidad base, categoría, dueño, posición preferida,
    /// isActive). since en UTC: cambios desde entonces (el aparato pide since = serverTimeUtc anterior − 5 minutos).
    /// </summary>
    [HttpGet("products"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<SyncPage<SyncProductDto>> Products([FromQuery] DateTime? since, [FromQuery] string? cursor,
        [FromQuery] int take = SyncRules.DefaultTake, CancellationToken ct = default)
        => sync.ProductsAsync(new SyncQuery(since, cursor, take, null), ct);

    /// <summary>
    /// Posiciones con su zona (código, nombre y tipo) del almacén indicado (warehousePublicId; sin él, de todos). isActive =
    /// posición, zona y almacén activos. since en UTC: cambios desde entonces (since = serverTimeUtc anterior − 5 minutos).
    /// </summary>
    [HttpGet("bins"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<SyncPage<SyncBinDto>> Bins([FromQuery] Guid? warehousePublicId, [FromQuery] DateTime? since, [FromQuery] string? cursor,
        [FromQuery] int take = SyncRules.DefaultTake, CancellationToken ct = default)
        => sync.BinsAsync(new SyncQuery(since, cursor, take, warehousePublicId), ct);

    /// <summary>
    /// Órdenes de compra abiertas (DRAFT, SENT, PARTIAL) con sus líneas: pedido, recibido y pendiente (descontando faltantes
    /// resueltos). Con since llegan también las que se cerraron o cancelaron (isActive = false). Filtro opcional
    /// warehousePublicId. since en UTC (since = serverTimeUtc anterior − 5 minutos).
    /// Además del módulo WMS exige el módulo PURCHASING y purchasing.view, igual que el recurso nativo /purchase-orders
    /// (sin purchasing.view → 403 de la política, sin cuerpo; módulo apagado → 403 'El módulo 'PURCHASING' no está
    /// habilitado para esta compañía.').
    /// </summary>
    [HttpGet("purchase-orders"), RequireModule(ModuleKeys.Purchasing), RequirePermission(PermissionCatalog.PurchasingView)]
    public Task<SyncPage<SyncPurchaseOrderDto>> PurchaseOrders([FromQuery] Guid? warehousePublicId, [FromQuery] DateTime? since,
        [FromQuery] string? cursor, [FromQuery] int take = SyncRules.DefaultTake, CancellationToken ct = default)
        => sync.PurchaseOrdersAsync(new SyncQuery(since, cursor, take, warehousePublicId), ct);

    /// <summary>
    /// Avisos de llegada EXPECTED con sus líneas. Con since llegan también los recibidos o cancelados (isActive = false).
    /// Filtro opcional warehousePublicId. since en UTC (since = serverTimeUtc anterior − 5 minutos).
    /// </summary>
    [HttpGet("asns"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<SyncPage<SyncAsnDto>> Asns([FromQuery] Guid? warehousePublicId, [FromQuery] DateTime? since, [FromQuery] string? cursor,
        [FromQuery] int take = SyncRules.DefaultTake, CancellationToken ct = default)
        => sync.AsnsAsync(new SyncQuery(since, cursor, take, warehousePublicId), ct);

    /// <summary>
    /// Tareas abiertas de la cola (PENDING, IN_PROGRESS) del almacén indicado, con producto, lote, cantidad, posiciones de
    /// origen y destino (sugerida) y asignado. Las abiertas llegan SIEMPRE; con since llegan además las cerradas desde entonces
    /// (isActive = false). since en UTC (since = serverTimeUtc anterior − 5 minutos).
    /// </summary>
    [HttpGet("warehouse-tasks"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<SyncPage<SyncWarehouseTaskDto>> WarehouseTasks([FromQuery] Guid? warehousePublicId, [FromQuery] DateTime? since,
        [FromQuery] string? cursor, [FromQuery] int take = SyncRules.DefaultTake, CancellationToken ct = default)
        => sync.WarehouseTasksAsync(new SyncQuery(since, cursor, take, warehousePublicId), ct);

    /// <summary>Categorías de producto (id, nombre, ascendente, isActive). since en UTC (since = serverTimeUtc anterior − 5 minutos).</summary>
    [HttpGet("product-categories"), RequirePermission(PermissionCatalog.InventoryView)]
    public Task<SyncPage<SyncProductCategoryDto>> ProductCategories([FromQuery] DateTime? since, [FromQuery] string? cursor,
        [FromQuery] int take = SyncRules.DefaultTake, CancellationToken ct = default)
        => sync.ProductCategoriesAsync(new SyncQuery(since, cursor, take, null), ct);
}
