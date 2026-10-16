using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 77 (P5) — plantillas de exportación a contabilidad (formato CSV o TXT, delimitador, formato de fecha, encabezado y columnas). Ver con billing.view; crear, cambiar y desactivar con billing.export.
/// Por ahora el único alcance es BILLING (facturación); los de compras, despachos y liquidación se suman con sus pantallas.
/// </summary>
[ApiController]
[Route("api/v1/export-templates")]
[Authorize]
[RequireModule(ModuleKeys.Billing)]
public sealed class ExportTemplatesController(ExportTemplateService templates) : ControllerBase
{
    /// <summary>Las plantillas activas del alcance (BILLING por omisión). La primera vez la compañía recibe «Formato estándar» con todas las columnas.</summary>
    [HttpGet, RequirePermission(PermissionCatalog.BillingView)]
    public Task<IReadOnlyList<ExportTemplateDto>> List([FromQuery] string? scope, CancellationToken ct) => templates.ListAsync(scope, ct);

    /// <summary>El catálogo de campos que se pueden exportar en el alcance (código, tipo, encabezado por omisión y etiqueta).</summary>
    [HttpGet("fields"), RequirePermission(PermissionCatalog.BillingView)]
    public IReadOnlyList<ExportFieldDto> Fields([FromQuery] string? scope) => templates.Fields(scope);

    [HttpGet("{publicId:guid}"), RequirePermission(PermissionCatalog.BillingView)]
    public Task<ExportTemplateDto> Get(Guid publicId, CancellationToken ct) => templates.GetAsync(publicId, ct);

    [HttpPost, RequirePermission(PermissionCatalog.BillingExport)]
    public Task<ExportTemplateDto> Create([FromBody] ExportTemplateRequest req, CancellationToken ct) => templates.CreateAsync(req, ct);

    /// <summary>Cambia la plantilla; lo que no se manda queda como estaba. 409 si el nombre ya lo usa otra.</summary>
    [HttpPut("{publicId:guid}"), RequirePermission(PermissionCatalog.BillingExport)]
    public Task<ExportTemplateDto> Update(Guid publicId, [FromBody] ExportTemplateRequest req, CancellationToken ct) => templates.UpdateAsync(publicId, req, ct);

    /// <summary>Desactiva la plantilla (no se borra). 409 si es la única del alcance.</summary>
    [HttpDelete("{publicId:guid}"), RequirePermission(PermissionCatalog.BillingExport)]
    public async Task<IActionResult> Delete(Guid publicId, CancellationToken ct)
    {
        await templates.DeactivateAsync(publicId, ct);
        return NoContent();
    }
}
