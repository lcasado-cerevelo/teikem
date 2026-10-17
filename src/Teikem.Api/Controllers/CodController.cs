using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 84 (P8) — ciclo COD (módulo COD). Ver con cod.view; cobrar con cod.collect; reconciliar con cod.reconcile. La reconciliación recibe los CÓDIGOS leídos de las hojas escaneadas (el número de la
/// factura del cliente o de la orden, que va en el código de barras) o escritos a mano; la lectura del código de barras de los archivos la hace la pantalla y aquí solo llegan los códigos.
/// </summary>
[ApiController]
[Route("api/v1/cod")]
[Authorize]
[RequireModule(ModuleKeys.Cod)]
public sealed class CodController(CodService cod) : ControllerBase
{
    /// <summary>Órdenes con COD: lo pedido, lo cobrado y lo que falta. Filtros: estatus del COD, cliente y búsqueda (orden, factura o cliente).</summary>
    [HttpGet("orders"), RequirePermission(PermissionCatalog.CodView)]
    public Task<PagedResult<CodOrderListItemDto>> Orders([FromQuery] string? status, [FromQuery] Guid? clientPublicId, [FromQuery] string? q, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
        => cod.OrdersAsync(status, clientPublicId, q, skip, take, ct);

    [HttpGet("orders/{orderPublicId:guid}"), RequirePermission(PermissionCatalog.CodView)]
    public Task<CodOrderDto> Order(Guid orderPublicId, CancellationToken ct) => cod.GetOrderAsync(orderPublicId, ct);

    /// <summary>Registra un cobro (parcial o total) de la orden.</summary>
    [HttpPost("orders/{orderPublicId:guid}/collections"), RequirePermission(PermissionCatalog.CodCollect)]
    public Task<CodOrderDto> Collect(Guid orderPublicId, [FromBody] CodCollectionRequest req, CancellationToken ct) => cod.CollectAsync(orderPublicId, req, ct);

    /// <summary>Cuadra lo cobrado de las órdenes a las que apuntan los códigos (número de factura u orden). Con dryRun solo dice qué pasaría.</summary>
    [HttpPost("reconcile"), RequirePermission(PermissionCatalog.CodReconcile)]
    public Task<CodReconcileResultDto> Reconcile([FromBody] CodReconcileRequest req, CancellationToken ct) => cod.ReconcileAsync(req, ct);

    [HttpGet("runs"), RequirePermission(PermissionCatalog.CodView)]
    public Task<PagedResult<CodRunListItemDto>> Runs([FromQuery] int skip, [FromQuery] int take, CancellationToken ct) => cod.RunsAsync(skip, take, ct);

    [HttpGet("runs/{publicId:guid}"), RequirePermission(PermissionCatalog.CodView)]
    public Task<CodRunDto> Run(Guid publicId, CancellationToken ct) => cod.GetRunAsync(publicId, ct);
}
