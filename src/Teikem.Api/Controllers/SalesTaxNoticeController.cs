using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 248 — aviso de exención de IVU al escoger el cliente de una orden de venta y en el resumen (módulo SALES_ORDERS; sales.create o sales.view). Va en un
/// archivo aparte para no tocar SalesOrdersController (Lote 247). Solo avisa: no bloquea nada; con el módulo TAX apagado responde NONE.
/// </summary>
[ApiController]
[Route("api/v1/sales-orders")]
[Authorize]
[RequireModule(ModuleKeys.SalesOrders)]
public sealed class SalesTaxNoticeController(SalesTaxNoticeService notices) : ControllerBase
{
    /// <summary>
    /// Nivel NONE, EXPIRING (la exención vence dentro del aviso de la compañía) o EXPIRED (venció y nada vigente cubre lo que cubría), con la fecha, los días, el
    /// certificado y el tipo de documento. 400 sin cliente («Indique el cliente.»); 404 si el cliente no existe o es de otra compañía.
    /// </summary>
    [HttpGet("tax-notice"), RequirePermission(PermissionCatalog.SalesCreate + "|" + PermissionCatalog.SalesView)]
    [ProducesResponseType(typeof(SalesTaxNoticeDto), StatusCodes.Status200OK), ProducesResponseType(StatusCodes.Status400BadRequest), ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<SalesTaxNoticeDto> Get([FromQuery] Guid? client, CancellationToken ct) => notices.GetAsync(client, ct);
}
