using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Domain.Integrations;
using Teikem.Infrastructure.Abstractions;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Exceptions;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 136 (P11) — los webhooks desde la API de un cliente: con su llave y el alcance «webhooks:manage» el cliente se suscribe a los eventos de lo suyo (order.status_changed, order.delivered,
/// invoice.issued), prueba su URL, rota su secreto y ve las salidas. Solo ve y toca los webhooks de SU integración (la del token). El personal los administra desde Integraciones.
/// </summary>
[ApiController]
[Route("api/v1/webhooks")]
[Authorize]
[RequireModule(ModuleKeys.Integrations)]
public sealed class WebhooksController(WebhookService webhooks, TenantContext tenant) : ControllerBase
{
    private Task<Guid> MyIntegration(CancellationToken ct)
    {
        if (!tenant.IsApiKey) throw new ForbiddenException("Esta ruta es para llaves de API; el personal administra los webhooks desde Integraciones.");
        return webhooks.IntegrationOfKeyAsync(User.FindFirst(TeikemClaims.ApiKeyId)?.Value ?? string.Empty, ct);
    }

    [HttpGet]
    public async Task<IReadOnlyList<WebhookDto>> List(CancellationToken ct) => await webhooks.ListAsync(await MyIntegration(ct), ct);

    [HttpGet("{id:guid}")]
    public async Task<WebhookDto> Get(Guid id, CancellationToken ct) => await webhooks.GetAsync(await MyIntegration(ct), id, ct);

    /// <summary>Crea un webhook: el secreto de firma viene en esta respuesta y en ninguna otra. 201.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] WebhookCreateRequest req, CancellationToken ct)
    {
        var created = await webhooks.CreateAsync(await MyIntegration(ct), req, ct);
        return Created($"/api/v1/webhooks/{created.Webhook.PublicId}", created);
    }

    [HttpPatch("{id:guid}")]
    public async Task<WebhookDto> Update(Guid id, [FromBody] WebhookPatchRequest req, CancellationToken ct) => await webhooks.UpdateAsync(await MyIntegration(ct), id, req, ct);

    /// <summary>Quita el webhook (se desactiva; el historial queda). 204.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        await webhooks.RemoveAsync(await MyIntegration(ct), id, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/test")]
    public async Task<WebhookTestResultDto> Test(Guid id, CancellationToken ct) => await webhooks.TestAsync(await MyIntegration(ct), id, ct);

    [HttpPost("{id:guid}/rotate-secret")]
    public async Task<WebhookCreatedDto> RotateSecret(Guid id, CancellationToken ct) => await webhooks.RotateSecretAsync(await MyIntegration(ct), id, ct);

    /// <summary>Las salidas del webhook (filtro: status PENDING / DELIVERED / UNDELIVERED / DISCARDED), con el cuerpo que se mandó.</summary>
    [HttpGet("{id:guid}/deliveries")]
    public async Task<WebhookDeliveryPageDto> Deliveries(Guid id, [FromQuery] string? status, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
        => await webhooks.DeliveriesAsync(await MyIntegration(ct), id, status, skip, take, true, ct);
}
