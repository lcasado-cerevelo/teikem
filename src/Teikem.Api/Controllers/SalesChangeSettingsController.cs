using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 249 (Lote E) — lo de las modificaciones de órdenes de venta que no cuelga de una orden (módulo SALES_ORDERS): la bandeja «Por aprobar» de la compañía
/// (paginada en el servidor) y los ajustes de segunda aprobación y adjunto obligatorio, de la compañía (admin.tenant) y de cada cliente (clients.update; null = hereda).
/// </summary>
[ApiController]
[Route("api/v1/sales-order-changes")]
[Authorize]
[RequireModule(ModuleKeys.SalesOrders)]
public sealed class SalesChangeSettingsController(SalesOrderChangeService changes) : ControllerBase
{
    /// <summary>
    /// Modificaciones pendientes de la segunda aprobación (skip/take, tope 200). q busca en el número de orden, el cliente, quién autorizó y el motivo; sort =
    /// registeredAt (más reciente primero, por omisión) | -registeredAt | order | -order | client | -client | delta | -delta.
    /// </summary>
    [HttpGet("pending"), RequirePermission(PermissionCatalog.SalesModifyApprove)]
    public Task<SalesChangePendingPageDto> Pending([FromQuery] int skip, [FromQuery] int take, [FromQuery] string? q, [FromQuery] string? sort, CancellationToken ct)
        => changes.PendingApprovalsAsync(new SalesChangePendingQuery(skip, take, q, sort), ct);

    /// <summary>Ajustes de la compañía: modo de segunda aprobación (NONE | ALWAYS | ABOVE_AMOUNT), monto y si el adjunto es obligatorio.</summary>
    [HttpGet("settings"), RequirePermission(PermissionCatalog.SalesView + "|" + PermissionCatalog.AdminTenant)]
    public Task<SalesChangeSettingsDto> Settings(CancellationToken ct) => changes.GetSettingsAsync(ct);

    /// <summary>Cambia los ajustes de la compañía (null = no cambia). 400 con el mensaje exacto si el modo o el monto no son válidos.</summary>
    [HttpPatch("settings"), RequirePermission(PermissionCatalog.AdminTenant)]
    public Task<SalesChangeSettingsDto> UpdateSettings([FromBody] SalesChangeSettingsRequest? req, CancellationToken ct) => changes.UpdateSettingsAsync(req, ct);

    /// <summary>Ajustes de un cliente (null = hereda de la compañía) y los vigentes que resultan.</summary>
    [HttpGet("settings/clients/{clientPublicId:guid}"), RequirePermission(PermissionCatalog.SalesView + "|" + PermissionCatalog.ClientsRead)]
    public Task<SalesClientChangeSettingsDto> ClientSettings(Guid clientPublicId, CancellationToken ct) => changes.GetClientSettingsAsync(clientPublicId, ct);

    /// <summary>Reemplaza los ajustes del cliente: modo "" o null = hereda; adjunto null = hereda.</summary>
    [HttpPatch("settings/clients/{clientPublicId:guid}"), RequirePermission(PermissionCatalog.ClientsUpdate)]
    public Task<SalesClientChangeSettingsDto> UpdateClientSettings(Guid clientPublicId, [FromBody] SalesClientChangeSettingsRequest? req, CancellationToken ct)
        => changes.UpdateClientSettingsAsync(clientPublicId, req, ct);
}
