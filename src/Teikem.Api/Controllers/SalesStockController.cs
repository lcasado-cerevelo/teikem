using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 235 — lo disponible al escoger productos en una venta (pedido guiado, «Nueva orden» y app de vendedores): los almacenes activos con el principal marcado y
/// lo «disponible para vender» por producto y almacén (la misma cuenta que usa Solicitar; un kit de oferta = kits completos que se arman con sus piezas). Una sola
/// consulta para todo el catálogo; la pantalla saca de aquí «Disponible: N», la validación de la cantidad y la nota «Hay disponible en …».
/// </summary>
[ApiController]
[Route("api/v1/sales-orders/stock")]
[Authorize]
[RequireModule(ModuleKeys.SalesOrders)]
public sealed class SalesStockController(SalesStockService stock) : ControllerBase
{
    [HttpGet, RequirePermission(PermissionCatalog.SalesView)]
    public Task<SalesStockDto> Get(CancellationToken ct) => stock.GetAsync(ct);
}
