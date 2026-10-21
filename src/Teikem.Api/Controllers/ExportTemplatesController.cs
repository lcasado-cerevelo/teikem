using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Teikem.Api.Auth;
using Teikem.Domain.Constants;
using Teikem.Infrastructure.Contracts;
using Teikem.Infrastructure.Services;

namespace Teikem.Api.Controllers;

/// <summary>
/// Lote 77 (P5) — plantillas de exportación a contabilidad (formato CSV o TXT, delimitador, formato de fecha, encabezado y columnas). Ver con billing.view; crear, cambiar y desactivar con billing.export.
/// Alcances: BILLING (facturación), COD_REMITTANCE (remesa COD), DRIVER_SETTLEMENT (liquidación a choferes), PURCHASE_ACCOUNTING y DISPATCH_ACCOUNTING (contabilización de compras y de despachos). Leerlas basta con ver facturación, COD, el pago a choferes o compras; cambiarlas exige billing.export o purchasing.manage.
/// </summary>
[ApiController]
[Route("api/v1/export-templates")]
[Authorize]
[RequireAnyModule(ModuleKeys.Billing, ModuleKeys.Cod, ModuleKeys.LtlGround, ModuleKeys.Purchasing, ModuleKeys.WmsLotSerial)]   // Lote 181: ya no exige Facturación a quien solo contabiliza compras o despachos
public sealed class ExportTemplatesController(ExportTemplateService templates) : ControllerBase
{
    // Lote 181: la contabilización de compras (purchasing.*) y la de despachos (billing.*) también leen y configuran sus plantillas aquí.
    private const string ReadAny = PermissionCatalog.BillingView + "|" + PermissionCatalog.CodView + "|" + PermissionCatalog.DriverPayView + "|" + PermissionCatalog.PurchasingView;
    private const string WriteAny = PermissionCatalog.BillingExport + "|" + PermissionCatalog.PurchasingManage;

    /// <summary>Las plantillas activas del alcance (BILLING por omisión). La primera vez la compañía recibe «Formato estándar» con todas las columnas.</summary>
    [HttpGet, RequirePermission(ReadAny)]
    public Task<IReadOnlyList<ExportTemplateDto>> List([FromQuery] string? scope, CancellationToken ct) => templates.ListAsync(scope, ct);

    /// <summary>El catálogo de campos que se pueden exportar en el alcance (código, tipo, encabezado por omisión y etiqueta).</summary>
    [HttpGet("fields"), RequirePermission(ReadAny)]
    public IReadOnlyList<ExportFieldDto> Fields([FromQuery] string? scope) => templates.Fields(scope);

    [HttpGet("{publicId:guid}"), RequirePermission(ReadAny)]
    public Task<ExportTemplateDto> Get(Guid publicId, CancellationToken ct) => templates.GetAsync(publicId, ct);

    [HttpPost, RequirePermission(WriteAny)]
    public Task<ExportTemplateDto> Create([FromBody] ExportTemplateRequest req, CancellationToken ct) => templates.CreateAsync(req, ct);

    /// <summary>Cambia la plantilla; lo que no se manda queda como estaba. 409 si el nombre ya lo usa otra.</summary>
    [HttpPut("{publicId:guid}"), RequirePermission(WriteAny)]
    public Task<ExportTemplateDto> Update(Guid publicId, [FromBody] ExportTemplateRequest req, CancellationToken ct) => templates.UpdateAsync(publicId, req, ct);

    /// <summary>Desactiva la plantilla (no se borra). 409 si es la única del alcance.</summary>
    [HttpDelete("{publicId:guid}"), RequirePermission(WriteAny)]
    public async Task<IActionResult> Delete(Guid publicId, CancellationToken ct)
    {
        await templates.DeactivateAsync(publicId, ct);
        return NoContent();
    }
}
