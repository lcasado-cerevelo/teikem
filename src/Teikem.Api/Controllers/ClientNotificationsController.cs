using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 98 (P10): avisos al cliente (el sender; nunca al consignatario). La COMPAÑÍA decide por cliente qué avisos y por qué canal: ver con clients.read, cambiar con clients.update (módulo CATALOG, en el
/// expediente del cliente). Sin configuración todo está apagado. El registro muestra los resúmenes que se le mandaron y el resultado de cada envío.
/// </summary>
[ApiController]
[Route("api/v1/clients/{publicId:guid}/notifications")]
[Authorize]
[RequireModule(ModuleKeys.Catalog)]
public sealed class ClientNotificationsController(ClientNotificationService notifications) : ControllerBase
{
    [HttpGet("settings"), RequirePermission(PermissionCatalog.ClientsRead)]
    public Task<ClientNotificationSettingsDto> Settings(Guid publicId, CancellationToken ct) => notifications.GetSettingsAsync(publicId, ct);

    [HttpPut("settings"), RequirePermission(PermissionCatalog.ClientsUpdate)]
    public Task<ClientNotificationSettingsDto> Save(Guid publicId, [FromBody] ClientNotificationSettingsRequest req, CancellationToken ct) => notifications.SaveSettingsAsync(publicId, req, ct);

    [HttpGet("log"), RequirePermission(PermissionCatalog.ClientsRead)]
    public Task<PagedResult<ClientNotificationDto>> Log(Guid publicId, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct) => notifications.ListAsync(publicId, skip, take, ct);
}
