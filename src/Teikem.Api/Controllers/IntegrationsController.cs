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
/// Lote 132 (P11) — Integraciones: de dónde (o a dónde) van los datos, con qué plantilla y cuándo. Módulo INTEGRATIONS. Ver con integrations.view; crear con integrations.create MÁS el
/// permiso de la familia de cada alcance (integrations.create.files…); administrar (editar, apagar, correr, subir un archivo) con integrations.manage; la bandeja con integrations.inbox.
/// Cada integración tiene su cuenta de servicio: lo que entra queda atribuido a ella. Nunca DELETE: se apaga.
/// </summary>
[ApiController]
[Route("api/v1/integrations")]
[Authorize]
[RequireModule(ModuleKeys.Integrations)]
public sealed class IntegrationsController(IntegrationService integrations, IntegrationRunner runner, IntegrationInboxService inbox, ApiKeyService apiKeys, ITenantContext tenant) : ControllerBase
{
    /// <summary>Lo que quien mira puede dar en el alta: alcances (y por qué alguno no se puede), conectores y a quién avisar.</summary>
    [HttpGet("options"), RequirePermission(PermissionCatalog.IntegrationsCreate + "|" + PermissionCatalog.IntegrationsManage)]
    public Task<IntegrationOptionsDto> Options(CancellationToken ct) => integrations.GetOptionsAsync(ct);

    [HttpGet, RequirePermission(PermissionCatalog.IntegrationsView)]
    public Task<IReadOnlyList<IntegrationDto>> List([FromQuery] bool includeInactive, CancellationToken ct) => integrations.ListAsync(includeInactive, ct);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.IntegrationsView)]
    public Task<IntegrationDto> Get(Guid publicId, CancellationToken ct) => integrations.GetAsync(publicId, ct);

    /// <summary>Crea la integración y su cuenta de servicio. 400 con el mensaje exacto; 403 si no puede dar un alcance; 409 si el nombre ya existe.</summary>
    [HttpPost, RequirePermission(PermissionCatalog.IntegrationsCreate)]
    public async Task<IActionResult> Create([FromBody] IntegrationCreateRequest req, CancellationToken ct)
    {
        var dto = await integrations.CreateAsync(req, ct);
        return CreatedAtAction(nameof(Get), new { publicId = dto.PublicId }, dto);
    }

    [HttpPatch("{publicId:guid}"), RequirePermission(PermissionCatalog.IntegrationsManage)]
    public Task<IntegrationDto> Update(Guid publicId, [FromBody] IntegrationPatchRequest req, CancellationToken ct) => integrations.UpdateAsync(publicId, req, ct);

    [HttpPost("{publicId:guid}/deactivate"), RequirePermission(PermissionCatalog.IntegrationsManage)]
    public Task<IntegrationDto> Deactivate(Guid publicId, CancellationToken ct) => integrations.SetActiveAsync(publicId, false, ct);

    [HttpPost("{publicId:guid}/reactivate"), RequirePermission(PermissionCatalog.IntegrationsManage)]
    public Task<IntegrationDto> Reactivate(Guid publicId, CancellationToken ct) => integrations.SetActiveAsync(publicId, true, ct);

    /// <summary>Corre ahora una integración de carpeta (lee los archivos nuevos). 409 si ya se está ejecutando; 422 si está apagada.</summary>
    [HttpPost("{publicId:guid}/run"), RequirePermission(PermissionCatalog.IntegrationsManage)]
    public Task<IntegrationRunDto> Run(Guid publicId, CancellationToken ct) => runner.RunAsync(publicId, IntegrationTriggers.Manual, tenant.UserId, ct);

    /// <summary>Sube un archivo a mano: se procesa igual que uno de la carpeta (hash, importador, bandeja).</summary>
    [HttpPost("{publicId:guid}/upload"), Consumes("multipart/form-data"), RequirePermission(PermissionCatalog.IntegrationsManage)]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<IntegrationRunDto> Upload(Guid publicId, IFormFile? file, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new ValidationException("file", IntegrationRunner.EmptyFileMessage);
        await using var stream = file.OpenReadStream();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        return await runner.UploadAsync(publicId, file.FileName, ms.ToArray(), tenant.UserId, ct);
    }

    /// <summary>Prueba la conexión con el buzón de la integración de correo (servidor, usuario, contraseña y carpeta) sin tocar los correos.</summary>
    [HttpPost("{publicId:guid}/email/test"), RequirePermission(PermissionCatalog.IntegrationsManage)]
    public Task<EmailTestResultDto> TestEmail(Guid publicId, CancellationToken ct) => integrations.TestEmailAsync(publicId, ct);

    /// <summary>Prueba la conexión de la integración (correo, SFTP o FTP) sin tocar los correos ni los archivos; en SFTP devuelve la huella que presenta el servidor para fijarla.</summary>
    [HttpPost("{publicId:guid}/test-connection"), RequirePermission(PermissionCatalog.IntegrationsManage)]
    public Task<ConnectionTestDto> TestConnection(Guid publicId, CancellationToken ct) => integrations.TestConnectionAsync(publicId, ct);

    /// <summary>Historial de corridas con sus archivos (las más recientes primero; hasta 100).</summary>
    [HttpGet("{publicId:guid}/runs"), RequirePermission(PermissionCatalog.IntegrationsView)]
    public Task<IReadOnlyList<IntegrationRunDto>> Runs(Guid publicId, [FromQuery] int take, CancellationToken ct) => integrations.RunsAsync(publicId, take <= 0 ? 20 : take, ct);

    // ---------------------------------------------------------------- llaves de API (integraciones de tipo API)

    /// <summary>Las llaves de la integración (nunca con el secreto: solo los últimos 4 caracteres).</summary>
    [HttpGet("{publicId:guid}/api-keys"), RequirePermission(PermissionCatalog.IntegrationsView)]
    public Task<IReadOnlyList<ApiKeyDto>> ApiKeys(Guid publicId, CancellationToken ct) => apiKeys.ListAsync(publicId, ct);

    /// <summary>Crea una llave: el SECRETO viene en esta respuesta y en ninguna otra (se guarda solo su hash). 409 si ya hay 10 activas.</summary>
    [HttpPost("{publicId:guid}/api-keys"), RequirePermission(PermissionCatalog.IntegrationsManage)]
    public Task<ApiKeyCreatedDto> CreateApiKey(Guid publicId, [FromBody] ApiKeyCreateRequest req, CancellationToken ct) => apiKeys.CreateAsync(publicId, req, ct);

    [HttpPatch("{publicId:guid}/api-keys/{keyId}"), RequirePermission(PermissionCatalog.IntegrationsManage)]
    public Task<ApiKeyDto> UpdateApiKey(Guid publicId, string keyId, [FromBody] ApiKeyPatchRequest req, CancellationToken ct) => apiKeys.UpdateAsync(publicId, keyId, req, ct);

    /// <summary>Revoca la llave (deja de servir de inmediato, aunque su token siga vivo). Rotar = crear otra y revocar la vieja.</summary>
    [HttpPost("{publicId:guid}/api-keys/{keyId}/revoke"), RequirePermission(PermissionCatalog.IntegrationsManage)]
    public Task<ApiKeyDto> RevokeApiKey(Guid publicId, string keyId, CancellationToken ct) => apiKeys.RevokeAsync(publicId, keyId, ct);

    /// <summary>Las últimas solicitudes de la llave (método, ruta, estatus y milisegundos).</summary>
    [HttpGet("{publicId:guid}/api-keys/{keyId}/log"), RequirePermission(PermissionCatalog.IntegrationsView)]
    public Task<IReadOnlyList<ApiLogDto>> ApiKeyLog(Guid publicId, string keyId, [FromQuery] int take, CancellationToken ct) => apiKeys.LogAsync(publicId, keyId, take <= 0 ? 50 : take, ct);

    // ---------------------------------------------------------------- bandeja

    /// <summary>La bandeja: lo que no se pudo procesar solo (filtros: integración y estatus PENDING / REPROCESSED / DISCARDED).</summary>
    [HttpGet("inbox"), RequirePermission(PermissionCatalog.IntegrationsInbox)]
    public Task<InboxPageDto> InboxList([FromQuery] Guid? integration, [FromQuery] string? status, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
        => inbox.ListAsync(integration, status, skip, take <= 0 ? 50 : take, ct);

    /// <summary>CSV con los pendientes (para devolverlos al cliente).</summary>
    [HttpGet("inbox/export"), RequirePermission(PermissionCatalog.IntegrationsInbox)]
    public async Task<IActionResult> InboxExport([FromQuery] Guid? integration, CancellationToken ct)
    {
        var csv = await inbox.ExportPendingAsync(integration, ct);
        return File(System.Text.Encoding.UTF8.GetPreamble().Concat(System.Text.Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv; charset=utf-8", "bandeja-integraciones.csv");
    }

    [HttpGet("inbox/{id:long}"), RequirePermission(PermissionCatalog.IntegrationsInbox)]
    public Task<InboxItemDto> InboxGet(long id, CancellationToken ct) => inbox.GetAsync(id, ct);

    /// <summary>Corrige las celdas de las filas del ítem (mismas filas, mismas columnas). 409 si ya se resolvió o se purgaron los datos.</summary>
    [HttpPatch("inbox/{id:long}"), RequirePermission(PermissionCatalog.IntegrationsInbox)]
    public Task<InboxItemDto> InboxFix(long id, [FromBody] InboxFixRequest req, CancellationToken ct) => inbox.FixAsync(id, req, ct);

    /// <summary>Vuelve a pasar el ítem por el importador; si ya cuadra nace la orden y el ítem queda REPROCESSED, si no sigue pendiente con el motivo nuevo.</summary>
    [HttpPost("inbox/{id:long}/reprocess"), RequirePermission(PermissionCatalog.IntegrationsInbox)]
    public Task<InboxReprocessResultDto> InboxReprocess(long id, CancellationToken ct) => inbox.ReprocessAsync(id, ct);

    [HttpPost("inbox/{id:long}/discard"), RequirePermission(PermissionCatalog.IntegrationsInbox)]
    public Task<InboxItemDto> InboxDiscard(long id, [FromBody] InboxDiscardRequest? req, CancellationToken ct) => inbox.DiscardAsync(id, req?.Note, ct);

    /// <summary>Reprocesa todos los pendientes de una integración (hasta 200 por vez).</summary>
    [HttpPost("{publicId:guid}/inbox/reprocess-all"), RequirePermission(PermissionCatalog.IntegrationsInbox)]
    public Task<InboxReprocessResultDto> InboxReprocessAll(Guid publicId, CancellationToken ct) => inbox.ReprocessAllAsync(publicId, ct);
}
