using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// 2026-10-12 — Ajuste múltiple AJM-###### (docs/ajuste-multiple-diseno.md): poner al día el inventario de un almacén por producto y posición, varias a la
/// vez, en una sola operación (todo o nada). TODAS las rutas exigen <c>inventory.adjust.multi</c>, que no viene en ningún rol por defecto: el administrador
/// lo da (o lo quita) a un rol desde Roles. Sin él, 403.
/// - <c>GET stock</c>: posiciones con existencia de los productos en el almacén (llena la pantalla).
/// - <c>POST</c>: aplica el documento. Diferencias calculadas en el servidor con el saldo bloqueado; lo que cambió desde que se abrió la pantalla →
///   409 stale_system_qty (lines[i]); por debajo de lo reservado → 409 insufficient_stock (lines[i]); sin diferencias → 400.
/// - <c>GET</c> y <c>GET {id}</c>: historial y detalle (En sistema / Hay ahora / Diferencia por línea).
/// </summary>
[ApiController]
[Route("api/v1/inventory/multi-adjustments")]
[Authorize]
[RequireModule(ModuleKeys.WmsLotSerial)]
[RequirePermission(PermissionCatalog.InventoryAdjustMulti)]
public sealed class MultiAdjustmentsController(MultiAdjustmentService service) : ControllerBase
{
    [HttpGet("stock")]
    public Task<IReadOnlyList<MultiAdjustmentProductDto>> Stock([FromQuery] Guid? warehouse, [FromQuery] Guid[]? products, CancellationToken ct)
        => service.StockAsync(warehouse, products ?? Array.Empty<Guid>(), ct);

    [HttpPost]
    public Task<MultiAdjustmentDetailDto> Create([FromBody] MultiAdjustmentRequest req, CancellationToken ct) => service.CreateAsync(req, ct);

    [HttpGet]
    public Task<MultiAdjustmentPageDto> List([FromQuery] Guid[]? warehouses, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] int[]? userIds,
        [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken ct = default)
        => service.ListAsync(new MultiAdjustmentQuery(warehouses, from, to, userIds, skip, take), ct);

    /// <summary>Por id interno: es el que trae el origen «Ajuste múltiple AJM-…» del Kárdex.</summary>
    [HttpGet("by-id/{id:int}")]
    public Task<MultiAdjustmentDetailDto> GetById(int id, CancellationToken ct) => service.GetByIdAsync(id, ct);

    [HttpGet("{publicId:guid}")]
    public Task<MultiAdjustmentDetailDto> Get(Guid publicId, CancellationToken ct) => service.GetAsync(publicId, ct);
}
