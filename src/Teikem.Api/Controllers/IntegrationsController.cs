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
public sealed class IntegrationsController(IntegrationService integrations, IntegrationRunner runner, IntegrationInboxService inbox, ApiKeyService apiKeys, WebhookService webhooks, IntegrationOutputService outputs, ITenantContext tenant) : ControllerBase
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
    /// <summary>2026-10-21: salida de un catálogo (productos…): anota TODOS los registros y corre la integración ahora («Enviar todo»).</summary>
    [HttpPost("{publicId:guid}/send-all"), RequirePermission(PermissionCatalog.IntegrationsManage)]
    public Task<IntegrationRunDto> SendAll(Guid publicId, CancellationToken ct) => outputs.SendAllAsync(publicId, tenant.UserId, ct);

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

    /// <summary>
    /// Vista previa de un archivo de muestra con el formato que se está configurando (Excel, JSON o XML con mapeo por ruta, o CSV): las hojas, el encabezado y las primeras 20 filas como las leería la plantilla.
    /// No guarda nada. «format» = la configuración en JSON (opcional); «delimiter» = el de la plantilla (por omisión coma); «hasHeader» como la plantilla.
    /// </summary>
    [HttpPost("format/preview"), Consumes("multipart/form-data"), RequirePermission(PermissionCatalog.IntegrationsCreate + "|" + PermissionCatalog.IntegrationsManage)]
    [RequestSizeLimit(4 * 1024 * 1024)]
    public async Task<FormatPreviewDto> PreviewFormat(IFormFile? file, [FromForm] string? format, [FromForm] string? delimiter, [FromForm] bool? hasHeader, CancellationToken ct)
    {
        if (file is null || file.Length == 0) throw new ValidationException("file", IntegrationRunner.EmptyFileMessage);
        FileFormatConfig? config = null;
        if (!string.IsNullOrWhiteSpace(format))
        {
            try { config = System.Text.Json.JsonSerializer.Deserialize<FileFormatConfig>(format, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)); }
            catch (System.Text.Json.JsonException) { throw new ValidationException("format", "El formato no es un JSON válido."); }
        }
        await using var stream = file.OpenReadStream();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        var sep = string.IsNullOrEmpty(delimiter) ? ',' : delimiter == "\\t" || delimiter == "\t" ? '\t' : delimiter[0];
        return integrations.PreviewFormat(ms.ToArray(), file.FileName, config, sep, hasHeader ?? true);
    }

    /// <summary>Prueba la conexión con el buzón de la integración de correo (servidor, usuario, contraseña y carpeta) sin tocar los correos.</summary>
    [HttpPost("{publicId:guid}/email/test"), RequirePermission(PermissionCatalog.IntegrationsManage)]
    public Task<EmailTestResultDto> TestEmail(Guid publicId, CancellationToken ct) => integrations.TestEmailAsync(publicId, ct);

    /// <summary>Prueba la conexión de la integración (correo, SFTP o FTP) sin tocar los correos ni los archivos; en SFTP devuelve la huella que presenta el servidor para fijarla.</summary>
    [HttpPost("{publicId:guid}/test-connection"), RequirePermission(PermissionCatalog.IntegrationsManage)]
    public async Task<ConnectionTestDto> TestConnection(Guid publicId, CancellationToken ct)
        => (await integrations.GetAsync(publicId, ct)).ConnectorKind == IntegrationConnectorKinds.Http ? await outputs.TestHttpAsync(publicId, ct) : await integrations.TestConnectionAsync(publicId, ct);   // HTTP (API saliente): manda un cuerpo de prueba

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

    // ---------------------------------------------------------------- webhooks de salida (Lote 136; integraciones de tipo API con el alcance webhooks:manage)

    /// <summary>Los webhooks de la integración (sin el secreto: solo los últimos 4 caracteres).</summary>
    [HttpGet("{publicId:guid}/webhooks"), RequirePermission(PermissionCatalog.IntegrationsView)]
    public Task<IReadOnlyList<WebhookDto>> Webhooks(Guid publicId, CancellationToken ct) => webhooks.ListAsync(publicId, ct);

    /// <summary>Crea un webhook: el SECRETO de firma viene en esta respuesta y en ninguna otra (se guarda cifrado). URL https pública y al menos un evento.</summary>
    [HttpPost("{publicId:guid}/webhooks"), RequirePermission(PermissionCatalog.IntegrationsManage)]
    public Task<WebhookCreatedDto> CreateWebhook(Guid publicId, [FromBody] WebhookCreateRequest req, CancellationToken ct) => webhooks.CreateAsync(publicId, req, ct);

    [HttpPatch("{publicId:guid}/webhooks/{webhookId:guid}"), RequirePermission(PermissionCatalog.IntegrationsManage)]
    public Task<WebhookDto> UpdateWebhook(Guid publicId, Guid webhookId, [FromBody] WebhookPatchRequest req, CancellationToken ct) => webhooks.UpdateAsync(publicId, webhookId, req, ct);

    /// <summary>Quita el webhook (se desactiva; el historial de salidas queda).</summary>
    [HttpDelete("{publicId:guid}/webhooks/{webhookId:guid}"), RequirePermission(PermissionCatalog.IntegrationsManage)]
    public async Task<IActionResult> RemoveWebhook(Guid publicId, Guid webhookId, CancellationToken ct)
    {
        await webhooks.RemoveAsync(publicId, webhookId, ct);
        return NoContent();
    }

    /// <summary>Reactiva un webhook suspendido.</summary>
    [HttpPost("{publicId:guid}/webhooks/{webhookId:guid}/resume"), RequirePermission(PermissionCatalog.IntegrationsManage)]
    public Task<WebhookDto> ResumeWebhook(Guid publicId, Guid webhookId, CancellationToken ct) => webhooks.ResumeAsync(publicId, webhookId, ct);

    /// <summary>Cambia el secreto de firma (el nuevo viene en la respuesta; el anterior deja de servir).</summary>
    [HttpPost("{publicId:guid}/webhooks/{webhookId:guid}/rotate-secret"), RequirePermission(PermissionCatalog.IntegrationsManage)]
    public Task<WebhookCreatedDto> RotateWebhookSecret(Guid publicId, Guid webhookId, CancellationToken ct) => webhooks.RotateSecretAsync(publicId, webhookId, ct);

    /// <summary>Manda un «webhook.ping» firmado a la URL y dice cómo respondió.</summary>
    [HttpPost("{publicId:guid}/webhooks/{webhookId:guid}/test"), RequirePermission(PermissionCatalog.IntegrationsManage)]
    public Task<WebhookTestResultDto> TestWebhook(Guid publicId, Guid webhookId, CancellationToken ct) => webhooks.TestAsync(publicId, webhookId, ct);

    // ---------------------------------------------------------------- salidas (bandeja de lo no entregado)

    /// <summary>Las salidas por webhook (filtros: integración, webhook y estatus PENDING / DELIVERED / UNDELIVERED / DISCARDED); «undelivered» = las que esperan atención.</summary>
    [HttpGet("outbox"), RequirePermission(PermissionCatalog.IntegrationsInbox)]
    public Task<WebhookDeliveryPageDto> Outbox([FromQuery] Guid? integration, [FromQuery] Guid? webhook, [FromQuery] string? status, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
        => webhooks.DeliveriesAsync(integration, webhook, status, skip, take, true, ct);

    /// <summary>Reintenta una salida no entregada (vuelve a pendiente con los intentos en cero).</summary>
    [HttpPost("outbox/{id:long}/retry"), RequirePermission(PermissionCatalog.IntegrationsInbox)]
    public Task<WebhookDeliveryDto> RetryOutbox(long id, CancellationToken ct) => webhooks.RetryAsync(id, ct);

    /// <summary>Descarta una salida que ya no se quiere entregar.</summary>
    [HttpPost("outbox/{id:long}/discard"), RequirePermission(PermissionCatalog.IntegrationsInbox)]
    public Task<WebhookDeliveryDto> DiscardOutbox(long id, CancellationToken ct) => webhooks.DiscardAsync(id, ct);

    // ---------------------------------------------------------------- integraciones de salida (Lote 137b)

    /// <summary>Arma el contenido de ejemplo de la salida con órdenes recientes del cliente, sin mandar nada.</summary>
    [HttpPost("{publicId:guid}/output/preview"), RequirePermission(PermissionCatalog.IntegrationsManage)]
    public Task<OutputPreviewDto> PreviewOutput(Guid publicId, CancellationToken ct) => outputs.PreviewAsync(publicId, ct);

    /// <summary>Los envíos de las integraciones de salida (filtros: integración y estatus PENDING / SENT / UNDELIVERED / DISCARDED); «undelivered» = los que esperan atención.</summary>
    [HttpGet("outputs"), RequirePermission(PermissionCatalog.IntegrationsInbox)]
    public Task<OutputPageDto> Outputs([FromQuery] Guid? integration, [FromQuery] string? status, [FromQuery] int skip, [FromQuery] int take, CancellationToken ct)
        => outputs.ListAsync(integration, status, skip, take, ct);

    /// <summary>Reintenta un envío no entregado (vuelve a pendiente con los intentos en cero).</summary>
    [HttpPost("outputs/{id:long}/retry"), RequirePermission(PermissionCatalog.IntegrationsInbox)]
    public Task<OutputItemDto> RetryOutput(long id, CancellationToken ct) => outputs.RetryAsync(id, ct);

    /// <summary>Descarta un envío que ya no se quiere mandar.</summary>
    [HttpPost("outputs/{id:long}/discard"), RequirePermission(PermissionCatalog.IntegrationsInbox)]
    public Task<OutputItemDto> DiscardOutput(long id, CancellationToken ct) => outputs.DiscardAsync(id, ct);

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
